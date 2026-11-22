using BilibiliLive.Danmu;
using BilibiliLiveRecordDownLoader.Application.Contracts.Danmaku;
using BilibiliLiveRecordDownLoader.Application.Contracts.Media;
using BilibiliLiveRecordDownLoader.Application.Danmaku;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using System.Xml;

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
		await stream.AcceptAsync([.. FlvSamples.Header, .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.SequenceHeader, 0), .. Frame(VideoPacket.KeyFrame, 0)]);
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
		string chapters = mediaTools.Chapters[Path.Combine(loop.RoomDirectory, "20260101_000000.mp4")];

		using (Assert.Multiple())
		{
			await Assert.That(firstFile.IsComplete).IsTrue();
			await Assert.That(firstFile.Source is DanmakuFileSource.Native { Header: { RoomId: RecordingLoopHarness.RoomId, Streamer: "streamer", Title: "title", Area: "area", ParentArea: "parent area", LiveId: "745" } }).IsTrue();
			await Assert.That(Describe(firstFile)).IsEqualTo("connection@0.000(anonymous,masked) 第一段@3.000 半秒之后@3.500");
			await Assert.That(Describe(secondFile)).IsEqualTo("connection@0.000(anonymous,masked) 第二段@1.000");
			await Assert.That(File.Exists(Path.Combine(loop.RoomDirectory, "20260101_000000.ass"))).IsTrue();
			await Assert.That(File.Exists(Path.Combine(loop.RoomDirectory, "20260101_000100.ass"))).IsTrue();
			await Assert.That(chapters).Contains("title=title");
			// 时长来自时间轴：第一段最后一帧在 59 秒
			await Assert.That(chapters).Contains("END=59000");
			await Assert.That(mediaTools.Remuxes.All(static remux => remux.Mode is RemuxMode.WithChapters { Path: var path } && !File.Exists(path))).IsTrue();
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
		await stream.AcceptAsync([.. FlvSamples.Header, .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.SequenceHeader, 0), .. Frame(VideoPacket.KeyFrame, 0)]);
		await SendSecondsAsync(loop, stream, 1, 3);
		await danmu.SendAsync(Comment("第一段"));

		// 加上偏移在第 5 秒，画面要录到那里，否则就在最后一帧之后了
		await SendSecondsAsync(loop, stream, 4, 6);
		await stream.EndAsync();
		await WaitForRemuxAsync(loop, mediaTools, 1);

		DanmakuFile file = await DanmakuXmlReader.ReadFileAsync(Path.Combine(loop.RoomDirectory, "20260101_000000.xml"), cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(file.Source is DanmakuFileSource.Native { Header.Offset.TotalSeconds: 2 }).IsTrue();
			await Assert.That(Describe(file)).IsEqualTo("connection@0.000(anonymous) 第一段@5.000");
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
		await stream.AcceptAsync([.. FlvSamples.Header, .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.SequenceHeader, 0), .. Frame(VideoPacket.KeyFrame, 0)]);
		await SendSecondsAsync(loop, stream, 1, 2);
		await danmu.SendAsync(Comment("第一次"));
		await stream.EndAsync();

		// 重连还没连上，这时没有打开的视频文件
		StreamRequest next = await loop.NextStreamAsync();
		loop.Time.Advance(TimeSpan.FromSeconds(1));
		await danmu.SendAsync(Comment("重连中"));
		await next.AcceptAsync([.. FlvSamples.Header, .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.SequenceHeader, 0), .. Frame(VideoPacket.KeyFrame, 5000)]);
		loop.Time.Advance(TimeSpan.FromMilliseconds(250));
		await danmu.SendAsync(Comment("第二次"));
		loop.Time.Advance(TimeSpan.FromMilliseconds(750));
		await next.SendAndWaitAsync(Frame(VideoPacket.KeyFrame, 6000));
		await next.EndAsync();
		await WaitForRemuxAsync(loop, mediaTools, 2);

		string[] files = [.. Directory.GetFiles(loop.RoomDirectory, "*.xml").Order(StringComparer.Ordinal)];
		DanmakuFile firstFile = await DanmakuXmlReader.ReadFileAsync(files[0], cancellationToken);
		DanmakuFile secondFile = await DanmakuXmlReader.ReadFileAsync(files[1], cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(string.Join(", ", files.Select(Path.GetFileName))).IsEqualTo("20260101_000000.xml, 20260101_000003.xml");
			await Assert.That(Describe(firstFile)).IsEqualTo("connection@0.000(anonymous) 第一次@2.000");
			await Assert.That(Describe(secondFile)).IsEqualTo("connection@0.000(anonymous) 第二次@0.250");
		}
	}

	[Test]
	[Timeout(30_000)]
	public async Task TooSmallVideoIsDeletedTogetherWithItsDanmakuAsync(CancellationToken cancellationToken)
	{
		RecordingMediaTools mediaTools = new();
		await using RecordingLoopHarness loop = Create(cancellationToken, mediaTools);
		FakeDanmuConnection danmu = await ConnectAsync(loop);

		StreamRequest stream = await loop.NextStreamAsync();
		await stream.AcceptAsync([.. FlvSamples.Header, .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.SequenceHeader, 0), .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.KeyFrame, 0)]);
		await danmu.SendAsync(Comment("很快断了"));
		await stream.EndAsync();
		_ = await loop.NextStreamAsync();
		string stem = Path.Combine(loop.RoomDirectory, "20260101_000000");

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
		(FakeDanmuConnection reconnected, TimeSpan disconnected) = await AdvanceUntilReconnectedAsync(loop);
		await reconnected.SendAsync(new DanmuMessage.Connected(0));
		string reconnectedAt = (2 + disconnected.TotalSeconds).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
		await reconnected.SendAsync(Comment("重连之后"));

		loop.GoOffline();
		await reconnected.SendAsync(new DanmuMessage.LiveStopped(false));
		await loop.WaitUntilAsync(static snapshot => !snapshot.Danmaku.IsRecording);

		string[] files = [.. Directory.GetFiles(loop.RoomDirectory, "*.xml").Order(StringComparer.Ordinal)];
		DanmakuFile first = await DanmakuXmlReader.ReadFileAsync(files[0], cancellationToken);
		DanmakuFile second = await DanmakuXmlReader.ReadFileAsync(files[1], cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(string.Join(", ", files.Select(Path.GetFileName))).IsEqualTo("20260101_000000.xml, 20260101_000010.xml");
			// 仅弹幕不加偏移
			await Assert.That(first.Source is DanmakuFileSource.Native { Header: { Title: "title", Offset.Ticks: 0 } }).IsTrue();
			await Assert.That(second.Source is DanmakuFileSource.Native { Header.Title: "new title" }).IsTrue();
			await Assert.That(Describe(first)).IsEqualTo("connection@0.000(anonymous,masked) 开播四秒@4.000");
			await Assert.That(Describe(second)).IsEqualTo($"connection@0.000(anonymous,masked) 换标题之后@2.000 gap@2.000-{reconnectedAt} connection@{reconnectedAt}(anonymous) 重连之后@{reconnectedAt}");
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

		DanmakuFile file = await DanmakuXmlReader.ReadFileAsync(Directory.GetFiles(loop.RoomDirectory, "*.xml").Single(), cancellationToken);

		await Assert.That(Describe(file)).IsEqualTo("connection@0.000(anonymous) title@7.000(new title)");
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
		loop.GoLive();
		await loop.WaitUntilAsync(static snapshot => snapshot.Danmaku.IsRecording);
		await loop.WaitUntilAsync(() => loop.Danmu.Options?.IncludeRaw is true, "raw messages are parsed");
		await danmu.SendAsync(new DanmuMessage.Raw("DANMU_MSG", """{"cmd":"DANMU_MSG","info":[1]}"""u8.ToArray()));
		loop.Time.Advance(TimeSpan.FromSeconds(1));
		await danmu.SendAsync(new DanmuMessage.Raw("ROOM_CHANGE", """{"cmd":"ROOM_CHANGE","data":{"title":"new title"}}"""u8.ToArray()));
		await danmu.SendAsync(new DanmuMessage.RoomChanged("new title", null, null));
		await danmu.SendAsync(new DanmuMessage.Raw("SEND_GIFT", """{"cmd":"SEND_GIFT"}"""u8.ToArray()));
		danmu.Close();
		(FakeDanmuConnection reconnected, _) = await AdvanceUntilReconnectedAsync(loop);
		await reconnected.SendAsync(new DanmuMessage.Connected(0));
		await reconnected.SendAsync(new DanmuMessage.LiveStopped(false));
		await loop.WaitUntilAsync(static snapshot => !snapshot.Danmaku.IsRecording);
		await loop.WaitUntilAsync(() => loop.Danmu.Options?.IncludeRaw is false, "raw messages are no longer parsed");

		// 写入者先停止解析原始消息再关闭文件，停止解析时文件可能还开着
		string raw = Path.Combine(loop.RoomDirectory, "20260101_000000.danmaku.jsonl");
		await loop.WaitUntilAsync(() => IsClosed(raw), "the raw file is closed");
		string[] lines = await File.ReadAllLinesAsync(raw, cancellationToken);
		string start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
		string second = new DateTimeOffset(2026, 1, 1, 0, 0, 1, TimeSpan.Zero).ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture);

		using (Assert.Multiple())
		{
			await Assert.That(Directory.GetFiles(loop.RoomDirectory, "*.jsonl").Length).IsEqualTo(1);
			await Assert.That(Directory.GetFiles(loop.RoomDirectory, "*.xml").Length).IsEqualTo(2);
			await Assert.That(lines.Length).IsEqualTo(6);
			await Assert.That(lines[0]).IsEqualTo("{\"t\":" + start + ",\"cmd\":\"@connected\",\"raw\":{\"anonymous\":true,\"masked\":true}}");
			await Assert.That(lines[1]).IsEqualTo("{\"t\":" + start + ",\"cmd\":\"DANMU_MSG\",\"raw\":{\"cmd\":\"DANMU_MSG\",\"info\":[1]}}");
			await Assert.That(lines[2]).IsEqualTo("{\"t\":" + second + ",\"cmd\":\"ROOM_CHANGE\",\"raw\":{\"cmd\":\"ROOM_CHANGE\",\"data\":{\"title\":\"new title\"}}}");
			await Assert.That(lines[3]).IsEqualTo("{\"t\":" + second + ",\"cmd\":\"SEND_GIFT\",\"raw\":{\"cmd\":\"SEND_GIFT\"}}");
			await Assert.That(lines[4]).IsEqualTo("{\"t\":" + second + ",\"cmd\":\"@disconnected\",\"raw\":null}");
			await Assert.That(lines[5]).EndsWith(",\"cmd\":\"@connected\",\"raw\":{\"anonymous\":true}}");
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
			await Assert.That(string.Join(", ", entries.Select(static entry => entry.Sequence))).IsEqualTo("1, 2");
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

	// 重连等待带随机抖动，按秒推进直到重新连接；推进的总时长即断开的时长
	private static async Task<(FakeDanmuConnection Connection, TimeSpan Advanced)> AdvanceUntilReconnectedAsync(RecordingLoopHarness loop)
	{
		Task<FakeDanmuConnection> next = loop.Danmu.NextConnectionAsync();
		TimeSpan advanced = TimeSpan.Zero;

		// 断开的时刻取房间察觉断开时的时钟，先等它察觉再推进，不然断开会记晚
		await loop.WaitUntilAsync(() => loop.Session.Danmaku.Feed.Status.DisconnectedAt is not null, "the room notices the disconnection");

		for (int step = 0; step < 20 && !next.IsCompleted; ++step)
		{
			await Task.Delay(50);

			if (!next.IsCompleted)
			{
				loop.Time.Advance(TimeSpan.FromSeconds(1));
				advanced += TimeSpan.FromSeconds(1);
			}
		}

		return (await next, advanced);
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

	// 一段要超过后处理删除的下限
	private static byte[] Frame(VideoPacket packet, uint timestamp)
	{
		return FlvSamples.PaddedVideo(packet, timestamp, packet is VideoPacket.KeyFrame ? KeyFrameSize : 0);
	}

	private static string Describe(DanmakuFile file)
	{
		return string.Join(' ', file.Records.Select(static record => record switch
		{
			DanmakuRecord.Comment comment => $"{comment.Text}@{Seconds(comment.Time)}",
			DanmakuRecord.Connection connection => $"connection@{Seconds(connection.Time)}({string.Join(',', new[] { connection.Anonymous ? "anonymous" : null, connection.Masked ? "masked" : null }.OfType<string>())})",
			DanmakuRecord.Gap gap => $"gap@{Seconds(gap.Time)}-{Seconds(gap.End)}",
			DanmakuRecord.TitleChange title => $"title@{Seconds(title.Time)}({title.Title})",
			_ => record.GetType().Name
		}));

		static string Seconds(TimeSpan time)
		{
			return time.TotalSeconds.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
		}
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
