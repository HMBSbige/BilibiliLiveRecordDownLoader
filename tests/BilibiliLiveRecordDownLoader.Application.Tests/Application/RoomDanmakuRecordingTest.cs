using BilibiliLive.Danmu;
using BilibiliLiveRecordDownLoader.Application.Contracts.Danmaku;
using BilibiliLiveRecordDownLoader.Application.Contracts.Media;
using BilibiliLiveRecordDownLoader.Application.Danmaku;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using System.Globalization;
using System.Xml;
using TUnit.Assertions.Enums;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class RoomDanmakuRecordingTest
{
	private const int KeyFrameSize = 40 << 10;

	[Test]
	[Timeout(30_000)]
	public async Task DanmakuFollowsTheVideoSegmentsWithTimesFromTheVideoAsync(CancellationToken cancellationToken)
	{
		RecordingMediaTools mediaTools = new();
		await using RecordingLoopHarness loop = Create(cancellationToken, mediaTools);
		loop.Config.WriteChaptersOnConvert = true;
		FakeDanmuConnection danmu = await ConnectAsync(loop, masked: true);

		StreamRequest stream = await loop.NextStreamAsync();
		await stream.AcceptAsync(Start(0));
		await SendSecondsAsync(loop, stream, 1, 3);
		await danmu.SendAsync(Comment("第一段"));
		loop.Time.Advance(TimeSpan.FromMilliseconds(500));
		await danmu.SendAsync(Comment("半秒之后"));

		// 弹幕稀少时空闲的写入者也按时落盘，录制中途的文件是完整的 XML
		// 写盘端和写入者都在别的线程上，可能落后于推进的时钟；时钟停下之后计时器不再到期，所以按帧推进直到落盘
		string first = Path.Combine(loop.RoomDirectory, "20260101_000000.xml");
		int second = 4;

		for (; second < 60 && !ContainsText(first, "半秒之后"); ++second)
		{
			await SendSecondsAsync(loop, stream, second, second);
		}

		await loop.WaitUntilAsync(() => ContainsText(first, "半秒之后"), "the first file is flushed before the segment ends");
		await SendSecondsAsync(loop, stream, second, 61);

		// 第 60 秒开始的一段要等下一个关键帧才提交，这条到那时才确定放进第二段
		await danmu.SendAsync(Comment("第二段"));
		await SendSecondsAsync(loop, stream, 62, 64);
		await stream.EndAsync();
		await WaitForRemuxAsync(loop, mediaTools, 2);

		DanmakuFile firstFile = await DanmakuXmlReader.ReadFileAsync(first, cancellationToken);
		DanmakuFile secondFile = await DanmakuXmlReader.ReadFileAsync(Path.Combine(loop.RoomDirectory, "20260101_000100.xml"), cancellationToken);
		DanmakuFileHeader header = await HeaderAsync(firstFile);
		string chapters = mediaTools.Chapters[Path.Combine(loop.RoomDirectory, "20260101_000000.mp4")];

		using (Assert.Multiple())
		{
			await Assert.That(firstFile.IsComplete).IsTrue();
			await Assert.That(header)
				.Member(static h => h.RoomId, static id => id.IsEqualTo(RecordingLoopHarness.RoomId))
				.And.Member(static h => h.Streamer, static streamer => streamer.IsEqualTo("streamer"))
				.And.Member(static h => h.Title, static title => title.IsEqualTo("title"))
				.And.Member(static h => h.Area, static area => area.IsEqualTo("area"))
				.And.Member(static h => h.ParentArea, static area => area.IsEqualTo("parent area"))
				.And.Member(static h => h.LiveId, static id => id.IsEqualTo("745"));
			await Assert.That(Describe(firstFile)).IsEquivalentTo(["connection@0.000(anonymous,masked)", "第一段@3.000", "半秒之后@3.500"], CollectionOrdering.Matching);
			await Assert.That(Describe(secondFile)).IsEquivalentTo(["connection@0.000(anonymous,masked)", "第二段@1.000"], CollectionOrdering.Matching);
			await Assert.That(Directory.GetFiles(loop.RoomDirectory, "*.ass").Select(static file => Path.GetFileName(file))).IsEquivalentTo(["20260101_000000.ass", "20260101_000100.ass"]);
			// 时长来自时间轴：第一段最后一帧在 59 秒
			await Assert.That(chapters).Contains("title=title").And.Contains("END=59000");
			await Assert.That(mediaTools.Remuxes).All(static remux => remux.Mode is RemuxMode.WithChapters { Path: var path } && !File.Exists(path));
		}
	}

	// 文件开头的连接状态是画面开头的状态，不加偏移
	[Test]
	[Timeout(30_000)]
	public async Task DanmakuFollowingTheVideoIsShiftedByTheOffsetAsync(CancellationToken cancellationToken)
	{
		RecordingMediaTools mediaTools = new();
		await using RecordingLoopHarness loop = Create(cancellationToken, mediaTools);
		loop.Config.DanmakuOffset = TimeSpan.FromSeconds(2);
		FakeDanmuConnection danmu = await ConnectAsync(loop);

		StreamRequest stream = await loop.NextStreamAsync();
		await stream.AcceptAsync(Start(0));
		await SendSecondsAsync(loop, stream, 1, 3);
		await danmu.SendAsync(Comment("第一段"));

		// 加上偏移在第 5 秒，画面要录到那里，否则就在最后一帧之后了
		await SendSecondsAsync(loop, stream, 4, 6);
		await stream.EndAsync();
		await WaitForRemuxAsync(loop, mediaTools, 1);

		DanmakuFile file = await DanmakuXmlReader.ReadFileAsync(Path.Combine(loop.RoomDirectory, "20260101_000000.xml"), cancellationToken);
		DanmakuFileHeader header = await HeaderAsync(file);

		using (Assert.Multiple())
		{
			await Assert.That(header.Offset).IsEqualTo(TimeSpan.FromSeconds(2));
			await Assert.That(Describe(file)).IsEquivalentTo(["connection@0.000(anonymous)", "第一段@5.000"], CollectionOrdering.Matching);
		}
	}

	[Test]
	[Timeout(30_000)]
	public async Task DanmakuDuringReconnectingIsLeftOutAndTheNextAttemptStartsFromItsOwnFileAsync(CancellationToken cancellationToken)
	{
		RecordingMediaTools mediaTools = new();
		await using RecordingLoopHarness loop = Create(cancellationToken, mediaTools);
		FakeDanmuConnection danmu = await ConnectAsync(loop);

		StreamRequest stream = await loop.NextStreamAsync();
		await stream.AcceptAsync(Start(0));
		await SendSecondsAsync(loop, stream, 1, 2);
		await danmu.SendAsync(Comment("第一次"));
		await stream.EndAsync();

		// 重连还没连上，这时没有打开的视频文件
		StreamRequest next = await loop.NextStreamAsync();
		loop.Time.Advance(TimeSpan.FromSeconds(1));
		await danmu.SendAsync(Comment("重连中"));
		await next.AcceptAsync(Start(5000));
		loop.Time.Advance(TimeSpan.FromMilliseconds(250));
		await danmu.SendAsync(Comment("第二次"));
		loop.Time.Advance(TimeSpan.FromMilliseconds(750));
		await next.SendAndWaitAsync(Frame(VideoPacket.KeyFrame, 6000));
		await next.EndAsync();
		await WaitForRemuxAsync(loop, mediaTools, 2);

		string[] files = [.. Directory.GetFiles(loop.RoomDirectory, "*.xml").Order(StringComparer.Ordinal)];
		await Assert.That(files.Select(static file => Path.GetFileName(file))).IsEquivalentTo(["20260101_000000.xml", "20260101_000003.xml"], CollectionOrdering.Matching);
		DanmakuFile firstFile = await DanmakuXmlReader.ReadFileAsync(files[0], cancellationToken);
		DanmakuFile secondFile = await DanmakuXmlReader.ReadFileAsync(files[1], cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(Describe(firstFile)).IsEquivalentTo(["connection@0.000(anonymous)", "第一次@2.000"], CollectionOrdering.Matching);
			await Assert.That(Describe(secondFile)).IsEquivalentTo(["connection@0.000(anonymous)", "第二次@0.250"], CollectionOrdering.Matching);
		}
	}

	[Test]
	[Timeout(30_000)]
	public async Task TooSmallVideoIsDeletedTogetherWithItsDanmakuAsync(CancellationToken cancellationToken)
	{
		RecordingMediaTools mediaTools = new();
		await using RecordingLoopHarness loop = Create(cancellationToken, mediaTools);
		FakeDanmuConnection danmu = await ConnectAsync(loop);
		string stem = Path.Combine(loop.RoomDirectory, "20260101_000000");

		StreamRequest stream = await loop.NextStreamAsync();
		await stream.AcceptAsync([.. FlvSamples.Header, .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.SequenceHeader, 0), .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.KeyFrame, 0)]);
		await danmu.SendAsync(Comment("很快断了"));

		// 先确认两个文件都建了，否则下面等它们消失的条件一开始就成立
		await loop.WaitUntilAsync(() => File.Exists(stem + ".flv") && File.Exists(stem + ".xml"), "the video and its danmaku are being written");
		await stream.EndAsync();
		_ = await loop.NextStreamAsync();

		await loop.WaitUntilAsync(() => !File.Exists(stem + ".flv") && !File.Exists(stem + ".xml"), "the small video and its danmaku are deleted");

		await Assert.That(mediaTools.Remuxes).IsEmpty();
	}

	[Test]
	[Timeout(30_000)]
	public async Task DanmakuOnlyFollowsTheLiveStatusAndSplitsOnTitleChangesAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = Create(cancellationToken, new RecordingMediaTools());
		loop.Settings.IsRecordingEnabled = false;
		loop.Config.SplitOnTitleChange = true;
		loop.Config.DanmakuOffset = TimeSpan.FromSeconds(3);
		FakeDanmuConnection danmu = await ConnectAsync(loop, masked: true);
		await loop.WaitUntilAsync(static snapshot => snapshot.Danmaku.IsRecording);

		loop.Time.Advance(TimeSpan.FromSeconds(4));
		await danmu.SendAsync(Comment("开播四秒"));
		loop.Time.Advance(TimeSpan.FromSeconds(6));
		await danmu.SendAsync(new DanmuMessage.RoomChanged("new title", null, null));
		loop.Time.Advance(TimeSpan.FromSeconds(2));
		await danmu.SendAsync(Comment("换标题之后"));

		danmu.Close();
		(FakeDanmuConnection reconnected, TimeSpan disconnected) = await loop.AdvanceUntilDanmuReconnectedAsync();
		await reconnected.SendAsync(new DanmuMessage.Connected(0));
		string reconnectedAt = Seconds(TimeSpan.FromSeconds(2) + disconnected);
		await reconnected.SendAsync(Comment("重连之后"));

		loop.GoOffline();
		await reconnected.SendAsync(new DanmuMessage.LiveStopped(false));
		await loop.WaitUntilAsync(static snapshot => !snapshot.Danmaku.IsRecording);

		string[] files = [.. Directory.GetFiles(loop.RoomDirectory, "*.xml").Order(StringComparer.Ordinal)];
		await Assert.That(files.Select(static file => Path.GetFileName(file))).IsEquivalentTo(["20260101_000000.xml", "20260101_000010.xml"], CollectionOrdering.Matching);
		DanmakuFile first = await DanmakuXmlReader.ReadFileAsync(files[0], cancellationToken);
		DanmakuFile second = await DanmakuXmlReader.ReadFileAsync(files[1], cancellationToken);
		DanmakuFileHeader firstHeader = await HeaderAsync(first);
		DanmakuFileHeader secondHeader = await HeaderAsync(second);

		using (Assert.Multiple())
		{
			// 仅弹幕不加偏移
			await Assert.That((firstHeader.Title, firstHeader.Offset)).IsEqualTo(("title", TimeSpan.Zero));
			await Assert.That(secondHeader.Title).IsEqualTo("new title");
			await Assert.That(Describe(first)).IsEquivalentTo(["connection@0.000(anonymous,masked)", "开播四秒@4.000"], CollectionOrdering.Matching);
			await Assert.That(Describe(second)).IsEquivalentTo(["connection@0.000(anonymous,masked)", "换标题之后@2.000", $"gap@2.000-{reconnectedAt}", $"connection@{reconnectedAt}(anonymous)", $"重连之后@{reconnectedAt}"], CollectionOrdering.Matching);
			await Assert.That(Directory.GetFiles(loop.RoomDirectory, "*.ass")).IsEmpty();
		}
	}

	[Test]
	[Timeout(30_000)]
	public async Task TitleChangeIsRecordedWhenDanmakuOnlyDoesNotSplitAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = Create(cancellationToken, new RecordingMediaTools());
		loop.Settings.IsRecordingEnabled = false;
		FakeDanmuConnection danmu = await ConnectAsync(loop);
		await loop.WaitUntilAsync(static snapshot => snapshot.Danmaku.IsRecording);

		loop.Time.Advance(TimeSpan.FromSeconds(7));
		await danmu.SendAsync(new DanmuMessage.RoomChanged("new title", "area", "parent area"));
		await danmu.SendAsync(new DanmuMessage.RoomChanged("new title", "other area", "parent area"));
		await danmu.SendAsync(new DanmuMessage.LiveStopped(false));
		await loop.WaitUntilAsync(static snapshot => !snapshot.Danmaku.IsRecording);

		string path = await Assert.That(Directory.GetFiles(loop.RoomDirectory, "*.xml")).HasSingleItem();
		DanmakuFile file = await DanmakuXmlReader.ReadFileAsync(path, cancellationToken);

		await Assert.That(Describe(file)).IsEquivalentTo(["connection@0.000(anonymous)", "title@7.000(new title)"], CollectionOrdering.Matching);
	}

	[Test]
	[Timeout(30_000)]
	public async Task RawMessagesOfTheWholeLiveGoToOneFileAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = Create(cancellationToken, new RecordingMediaTools());
		loop.Settings.IsRecordingEnabled = false;
		loop.Config.SaveRawDanmaku = true;
		loop.Config.SplitOnTitleChange = true;

		// 开播之后才开始记录，文件开头补记之前的连接
		loop.GoOffline();
		FakeDanmuConnection danmu = await ConnectAsync(loop, masked: true);

		// 首次状态查询的应答晚于开播到达时，会把房间改回未开播
		await loop.WaitUntilAsync(static snapshot => snapshot.LiveStatus is LiveStatus.Idle);
		loop.GoLive();
		await loop.WaitUntilAsync(static snapshot => snapshot.Danmaku.IsRecording);
		await loop.WaitUntilAsync(() => loop.Danmu.Options?.IncludeRaw is true, "raw messages are parsed");
		await danmu.SendAsync(new DanmuMessage.Raw("DANMU_MSG", """{"cmd":"DANMU_MSG","info":[1]}"""u8.ToArray()));
		loop.Time.Advance(TimeSpan.FromSeconds(1));
		await danmu.SendAsync(new DanmuMessage.Raw("ROOM_CHANGE", """{"cmd":"ROOM_CHANGE","data":{"title":"new title"}}"""u8.ToArray()));
		await danmu.SendAsync(new DanmuMessage.RoomChanged("new title", null, null));
		await danmu.SendAsync(new DanmuMessage.Raw("SEND_GIFT", """{"cmd":"SEND_GIFT"}"""u8.ToArray()));
		danmu.Close();
		(FakeDanmuConnection reconnected, TimeSpan disconnected) = await loop.AdvanceUntilDanmuReconnectedAsync();
		await reconnected.SendAsync(new DanmuMessage.Connected(0));
		await reconnected.SendAsync(new DanmuMessage.LiveStopped(false));
		await loop.WaitUntilAsync(static snapshot => !snapshot.Danmaku.IsRecording);
		await loop.WaitUntilAsync(() => loop.Danmu.Options?.IncludeRaw is false, "raw messages are no longer parsed");

		// 写入者先停止解析原始消息再关闭文件，停止解析时文件可能还开着
		string raw = Path.Combine(loop.RoomDirectory, "20260101_000000.danmaku.jsonl");
		await loop.WaitUntilAsync(() => IsClosed(raw), "the raw file is closed");
		string[] lines = await File.ReadAllLinesAsync(raw, cancellationToken);
		string start = UnixMilliseconds(TimeSpan.Zero);
		string second = UnixMilliseconds(TimeSpan.FromSeconds(1));
		string reconnectedAt = UnixMilliseconds(TimeSpan.FromSeconds(1) + disconnected);

		using (Assert.Multiple())
		{
			await Assert.That(Directory.GetFiles(loop.RoomDirectory, "*.jsonl")).IsEquivalentTo([raw]);
			await Assert.That(Directory.GetFiles(loop.RoomDirectory, "*.xml")).Count().IsEqualTo(2);
			await Assert.That(lines).IsEquivalentTo(
			[
				RawLine(start, "@connected", """{"anonymous":true,"masked":true}"""),
				RawLine(start, "DANMU_MSG", """{"cmd":"DANMU_MSG","info":[1]}"""),
				RawLine(second, "ROOM_CHANGE", """{"cmd":"ROOM_CHANGE","data":{"title":"new title"}}"""),
				RawLine(second, "SEND_GIFT", """{"cmd":"SEND_GIFT"}"""),
				RawLine(second, "@disconnected", "null"),
				RawLine(reconnectedAt, "@connected", """{"anonymous":true}""")
			], CollectionOrdering.Matching);
		}
	}

	[Test]
	[Timeout(30_000)]
	public async Task ParsingIsOnOnlyWhileRecordingDanmakuOrSubscribedAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = Create(cancellationToken, new RecordingMediaTools());
		loop.Settings.IsDanmakuEnabled = false;
		FakeDanmuConnection danmu = await ConnectAsync(loop);
		_ = await loop.NextStreamAsync();
		DanmuReceiveOptions options = await Assert.That(loop.Danmu.Options).IsNotNull();
		bool idle = options.IncludeChat;

		IDisposable subscription = loop.Session.Danmaku.Subscribe();
		bool subscribed = options.IncludeChat;
		await danmu.SendAsync(Comment("看看"));
		int seen = loop.Session.Danmaku.Feed.GetEntries(0).Count;
		subscription.Dispose();
		bool unsubscribed = options.IncludeChat;
		int cleared = loop.Session.Danmaku.Feed.GetEntries(0).Count;

		loop.Session.UpdateSettings(loop.Settings with { IsDanmakuEnabled = true });
		bool recording = options.IncludeChat;
		bool raw = options.IncludeRaw;

		loop.Session.UpdateSettings(loop.Settings with { IsDanmakuEnabled = true, IsRecordingEnabled = false });
		await loop.WaitUntilAsync(static snapshot => snapshot.Danmaku.IsRecording);
		bool textOnly = options.IncludeChat;

		loop.Session.UpdateSettings(loop.Settings with { IsDanmakuEnabled = false, IsRecordingEnabled = false });
		await loop.WaitUntilAsync(static snapshot => !snapshot.Danmaku.IsRecording);

		using (Assert.Multiple())
		{
			await Assert.That(idle).IsFalse();
			await Assert.That(subscribed).IsTrue();
			await Assert.That(seen).IsEqualTo(1);
			await Assert.That(unsubscribed).IsFalse();
			await Assert.That(cleared).IsEqualTo(0);
			await Assert.That(recording).IsTrue();
			await Assert.That(raw).IsFalse();
			await Assert.That(textOnly).IsTrue();
			await Assert.That(options.IncludeChat).IsFalse();
		}
	}

	[Test]
	[Timeout(30_000)]
	public async Task FeedKeepsRecentEntriesWhileRecordingAndReportsTheConnectionAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = Create(cancellationToken, new RecordingMediaTools());
		loop.Settings.IsRecordingEnabled = false;
		FakeDanmuConnection danmu = await ConnectAsync(loop, masked: true);
		await loop.WaitUntilAsync(static snapshot => snapshot.Danmaku.IsRecording);

		await danmu.SendAsync(Comment("录着呢"));
		await danmu.SendAsync(new DanmuMessage.SuperChat(1, new DanmuSender(42, "老板", GuardLevel.Captain, null), "醒目留言", 30000, TimeSpan.FromSeconds(60), DateTimeOffset.UnixEpoch));

		using IDisposable subscription = loop.Session.Danmaku.Subscribe();
		IReadOnlyList<LiveDanmakuEntryDto> entries = loop.Session.Danmaku.Feed.GetEntries(0);
		DanmakuFeedStatus status = loop.Session.Danmaku.Feed.Status;
		RoomDanmakuSnapshot snapshot = loop.Snapshot.Danmaku;

		using (Assert.Multiple())
		{
			await Assert.That(entries.Select(static entry => entry.Sequence)).IsEquivalentTo([1L, 2], CollectionOrdering.Matching);
			await Assert.That(entries[1].Entry).IsEqualTo(new DanmakuEntryDto.SuperChat(1, new DanmuSender(42, "老板", GuardLevel.Captain, null), "醒目留言", 30000, TimeSpan.FromSeconds(60)));
			await Assert.That(status).IsEqualTo(new DanmakuFeedStatus(DanmakuConnectionState.Connected, 1, 0, true, 2, null));
			await Assert.That(snapshot).IsEqualTo(new RoomDanmakuSnapshot(true));
		}
	}

	private static RecordingLoopHarness Create(CancellationToken cancellationToken, RecordingMediaTools mediaTools)
	{
		RecordingLoopHarness loop = RecordingLoopHarness.CreateSplitting(cancellationToken, mediaTools);
		loop.Settings.IsDanmakuEnabled = true;
		return loop;
	}

	private static async Task<FakeDanmuConnection> ConnectAsync(RecordingLoopHarness loop, bool masked = false)
	{
		loop.Session.Start();
		FakeDanmuConnection danmu = await loop.Danmu.NextConnectionAsync();
		await danmu.SendAsync(new DanmuMessage.Connected(0));

		if (masked)
		{
			await danmu.SendAsync(new DanmuMessage.LoginNotice("为保护用户隐私，未登录无法查看他人昵称"));
		}

		return danmu;
	}

	private static DanmuMessage.Comment Comment(string text)
	{
		return new DanmuMessage.Comment(new DanmuSender(0, "观***", GuardLevel.None, null), text, 1, 25, 0xFFFFFF, DateTimeOffset.UnixEpoch, "a16fe0dd", DanmuCommentKind.Text, false);
	}

	private static async Task SendSecondsAsync(RecordingLoopHarness loop, StreamRequest stream, int from, int to)
	{
		for (int second = from; second <= to; ++second)
		{
			loop.Time.Advance(TimeSpan.FromSeconds(1));
			await stream.SendAndWaitAsync(Frame(second % 2 is 0 ? VideoPacket.KeyFrame : VideoPacket.InterFrame, (uint)(second * 1000)));
		}
	}

	private static byte[] Start(uint timestamp)
	{
		return [.. FlvSamples.Header, .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.SequenceHeader, 0), .. Frame(VideoPacket.KeyFrame, timestamp)];
	}

	// 一段要超过后处理删除的下限
	private static byte[] Frame(VideoPacket packet, uint timestamp)
	{
		return FlvSamples.PaddedVideo(packet, timestamp, packet is VideoPacket.KeyFrame ? KeyFrameSize : 0);
	}

	private static async Task<DanmakuFileHeader> HeaderAsync(DanmakuFile file)
	{
		DanmakuFileSource.Native? native = await Assert.That(file.Source).IsTypeOf<DanmakuFileSource.Native>();
		return native!.Header;
	}

	private static IEnumerable<string> Describe(DanmakuFile file)
	{
		return file.Records.Select(static record => record switch
		{
			DanmakuRecord.Comment comment => $"{comment.Text}@{Seconds(comment.Time)}",
			DanmakuRecord.Connection connection => $"connection@{Seconds(connection.Time)}({string.Join(',', new[] { connection.Anonymous ? "anonymous" : null, connection.Masked ? "masked" : null }.OfType<string>())})",
			DanmakuRecord.Gap gap => $"gap@{Seconds(gap.Time)}-{Seconds(gap.End)}",
			DanmakuRecord.TitleChange title => $"title@{Seconds(title.Time)}({title.Title})",
			_ => record.GetType().Name
		});
	}

	private static string Seconds(TimeSpan time)
	{
		return time.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture);
	}

	// CreateSplitting 把时钟拨到这一刻
	private static string UnixMilliseconds(TimeSpan sinceStart)
	{
		return (new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) + sinceStart).ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
	}

	private static string RawLine(string time, string command, string raw)
	{
		return $$"""{"t":{{time}},"cmd":"{{command}}","raw":{{raw}}}""";
	}

	// 写入者还开着文件时，Windows 报共享冲突，Unix 拿不到排他锁；用 FileShare.Read 打开在 Unix 上探测不出来
	private static bool IsClosed(string path)
	{
		try
		{
			using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.None);
			return true;
		}
		catch (IOException)
		{
			return false;
		}
	}

	// 文件不完整时 XmlException 让测试失败
	private static bool ContainsText(string path, string text)
	{
		if (!File.Exists(path))
		{
			return false;
		}

		// 写入者以 FileShare.Read 打开，读的一方要允许写共享
		using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
		using XmlReader reader = XmlReader.Create(stream);
		bool found = false;

		while (reader.Read())
		{
			found |= reader is { NodeType: XmlNodeType.Text, Value: var value } && value == text;
		}

		return found;
	}

	private static async Task WaitForRemuxAsync(RecordingLoopHarness loop, RecordingMediaTools mediaTools, int count)
	{
		await loop.WaitUntilAsync(() => mediaTools.Remuxes.Count >= count, $"{count} remuxes are queued");
	}
}
