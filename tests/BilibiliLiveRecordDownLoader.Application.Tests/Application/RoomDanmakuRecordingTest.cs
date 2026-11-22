using BilibiliLive.Danmu;
using BilibiliLiveRecordDownLoader.Application.Contracts.Danmaku;
using BilibiliLiveRecordDownLoader.Application.Contracts.Media;
using BilibiliLiveRecordDownLoader.Application.Danmaku;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using System.Xml;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

// 用真实的房间、录制器和写入者跑完整的弹幕录制；弹幕连接由 FakeDanmuSource 代替，时钟由测试推进
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

		// 录制中途的文件任何时候都是完整的 XML
		string first = Path.Combine(loop.RoomDirectory, "20260101_000000.xml");
		await Assert.That(IsWellFormed(first)).IsTrue();

		loop.Time.Advance(TimeSpan.FromMilliseconds(500));
		await danmu.SendAsync(Comment("半秒之后"));
		await SendSecondsAsync(loop, stream, 4, 61);

		// 第 60 秒开始的一段要等下一个关键帧才提交，这条到那时才确定放进第二段
		await danmu.SendAsync(Comment("第二段"));
		await SendSecondsAsync(loop, stream, 62, 64);
		await stream.EndAsync();
		await WaitForRemuxAsync(mediaTools, 2);

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
			await Assert.That(mediaTools.Remuxes.All(static remux => remux.Chapters is { } path && !File.Exists(path))).IsTrue();
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
		await WaitForRemuxAsync(mediaTools, 2);

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

		await WaitUntilAsync(() => !File.Exists(stem + ".flv") && !File.Exists(stem + ".xml"));

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

		// 先开始记录再连上，连接建立的那一行在最前面
		loop.Session.Start();
		await loop.WaitUntilAsync(static snapshot => snapshot.Danmaku.IsRecording);
		await WaitUntilAsync(() => loop.Danmu.Options?.IncludeRaw is true);
		FakeDanmuConnection danmu = await loop.Danmu.NextConnectionAsync();
		await danmu.SendAsync(new DanmuMessage.Connected(0));
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
		await WaitUntilAsync(() => loop.Danmu.Options?.IncludeRaw is false);

		// 写入者先停止解析原始消息再关闭文件，停止解析时文件可能还开着
		string raw = Path.Combine(loop.RoomDirectory, "20260101_000000.danmaku.jsonl");
		await WaitUntilAsync(() => IsClosed(raw));
		string[] lines = await File.ReadAllLinesAsync(raw, cancellationToken);
		string start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
		string second = new DateTimeOffset(2026, 1, 1, 0, 0, 1, TimeSpan.Zero).ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture);

		using (Assert.Multiple())
		{
			await Assert.That(Directory.GetFiles(loop.RoomDirectory, "*.jsonl").Length).IsEqualTo(1);
			await Assert.That(Directory.GetFiles(loop.RoomDirectory, "*.xml").Length).IsEqualTo(2);
			await Assert.That(lines.Length).IsEqualTo(6);
			await Assert.That(lines[0]).IsEqualTo("{\"t\":" + start + ",\"cmd\":\"@connected\",\"raw\":{\"uid\":0}}");
			await Assert.That(lines[1]).IsEqualTo("{\"t\":" + start + ",\"cmd\":\"DANMU_MSG\",\"raw\":{\"cmd\":\"DANMU_MSG\",\"info\":[1]}}");
			await Assert.That(lines[2]).IsEqualTo("{\"t\":" + second + ",\"cmd\":\"ROOM_CHANGE\",\"raw\":{\"cmd\":\"ROOM_CHANGE\",\"data\":{\"title\":\"new title\"}}}");
			await Assert.That(lines[3]).IsEqualTo("{\"t\":" + second + ",\"cmd\":\"SEND_GIFT\",\"raw\":{\"cmd\":\"SEND_GIFT\"}}");
			await Assert.That(lines[4]).IsEqualTo("{\"t\":" + second + ",\"cmd\":\"@disconnected\",\"raw\":null}");
			await Assert.That(lines[5]).EndsWith(",\"cmd\":\"@connected\",\"raw\":{\"uid\":0}}");
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
		DanmuReceiveOptions options = loop.Danmu.Options!;
		bool idle = options.IncludeChat;

		IDisposable subscription = loop.Session.Danmaku.Subscribe();
		bool subscribed = options.IncludeChat;
		await danmu.SendAsync(Comment("看看"));
		int seen = loop.Session.Danmaku.Feed.GetEntries(0).Count;
		subscription.Dispose();
		bool unsubscribed = options.IncludeChat;
		int cleared = loop.Session.Danmaku.Feed.GetEntries(0).Count;

		// 录制循环在跑，打开录弹幕之后就解析；不保存原始消息时不产出原始消息
		loop.Session.UpdateSettings(loop.Settings with { IsDanmakuEnabled = true });
		bool recording = options.IncludeChat;
		bool raw = options.IncludeRaw;

		// 关掉录制之后改为仅录弹幕，仍然解析
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

		// 录弹幕时订阅之前的条目也在
		using IDisposable subscription = loop.Session.Danmaku.Subscribe();
		IReadOnlyList<LiveDanmakuEntryDto> entries = loop.Session.Danmaku.Feed.GetEntries(0);
		DanmakuFeedStatus status = loop.Session.Danmaku.Feed.Status;
		RoomDanmakuSnapshot snapshot = loop.Snapshot.Danmaku;

		using (Assert.Multiple())
		{
			await Assert.That(string.Join(", ", entries.Select(static entry => entry.Sequence))).IsEqualTo("1, 2");
			await Assert.That(entries[1].Entry).IsEqualTo(new DanmakuEntryDto.SuperChat(1, new DanmuSender(42, "老板", GuardLevel.Captain, null), "醒目留言", 30000, TimeSpan.FromSeconds(60)));
			await Assert.That(status).IsEqualTo(new DanmakuFeedStatus(DanmakuConnectionState.Connected, 0, true, 2, null, true));
			await Assert.That(snapshot).IsEqualTo(new RoomDanmakuSnapshot(true, 0, true));
		}
	}

	private static RecordingLoopHarness Create(CancellationToken cancellationToken, RecordingMediaTools mediaTools)
	{
		RecordingLoopHarness loop = new(cancellationToken, mediaTools: mediaTools);
		loop.Time.SetUtcNow(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
		loop.Time.SetLocalTimeZone(TimeZoneInfo.Utc);
		loop.Config.SplitMode = RecordingSplitMode.Duration;
		loop.Config.SplitDuration = TimeSpan.FromMinutes(1);
		loop.Config.ConvertToMp4 = true;
		loop.Config.IncludeTitleInFileName = false;
		loop.Settings.IsDanmakuEnabled = true;
		loop.Settings.StreamIdleTimeout = TimeSpan.FromHours(1);
		return loop;
	}

	// 开始监控，状态查询得知已开播；弹幕连接先连上
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

	// 断开后的重连等待带随机抖动，按秒推进直到重新连接，返回推进的总时长，即断开的时长
	private static async Task<(FakeDanmuConnection Connection, TimeSpan Advanced)> AdvanceUntilReconnectedAsync(RecordingLoopHarness loop)
	{
		Task<FakeDanmuConnection> next = loop.Danmu.NextConnectionAsync();
		TimeSpan advanced = TimeSpan.Zero;

		// 断开的时刻取房间察觉断开时的时钟，先等它察觉再推进，不然断开会记晚
		await WaitUntilAsync(() => loop.Session.Danmaku.Feed.Status.DisconnectedAt is not null);

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
		return new DanmuMessage.Comment(new DanmuSender(0, "观***", GuardLevel.None, null), text, 1, 25, 0xFFFFFF, DateTimeOffset.UnixEpoch, "5e7c63ca70d385b033f842f35a6b02c84761", "a16fe0dd", DanmuCommentKind.Text, false);
	}

	// 每秒一帧，偶数秒是关键帧，按帧推进时钟
	private static async Task SendSecondsAsync(RecordingLoopHarness loop, StreamRequest stream, int from, int to)
	{
		for (int second = from; second <= to; ++second)
		{
			loop.Time.Advance(TimeSpan.FromSeconds(1));
			await stream.SendAndWaitAsync(Frame(second % 2 is 0 ? VideoPacket.KeyFrame : VideoPacket.InterFrame, (uint)(second * 1000)));
		}
	}

	// 关键帧补到足够大，一段超过后处理删除的下限
	private static byte[] Frame(VideoPacket packet, uint timestamp)
	{
		byte[] frame = FlvSamples.Video(VideoStyle.Avc, packet, timestamp);
		int padding = packet is VideoPacket.KeyFrame ? KeyFrameSize : 0;
		return FlvSamples.Tag(FlvSamples.VideoTag, timestamp, [.. frame.AsSpan(11, frame.Length - 15), .. new byte[padding]]);
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

	// 写入者开着文件时不允许别人以只读共享打开
	private static bool IsClosed(string path)
	{
		try
		{
			using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
			return true;
		}
		catch (IOException)
		{
			return false;
		}
	}

	private static bool IsWellFormed(string path)
	{
		try
		{
			using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			using XmlReader reader = XmlReader.Create(stream);

			while (reader.Read())
			{
			}

			return true;
		}
		catch (XmlException)
		{
			return false;
		}
	}

	private static async Task WaitForRemuxAsync(RecordingMediaTools mediaTools, int count)
	{
		await WaitUntilAsync(() => mediaTools.Remuxes.Count >= count);
	}

	private static async Task WaitUntilAsync(Func<bool> condition)
	{
		using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));

		while (!condition())
		{
			await Task.Delay(10, timeout.Token);
		}
	}
}
