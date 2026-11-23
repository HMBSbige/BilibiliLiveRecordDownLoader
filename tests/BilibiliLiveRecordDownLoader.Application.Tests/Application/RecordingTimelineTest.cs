using BilibiliLiveRecordDownLoader.Application.Recording;
using BilibiliLiveRecordDownLoader.Application.Recording.Splitting;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using System.Globalization;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class RecordingTimelineTest
{
	private const uint FrameInterval = 100;
	private const int FramesPerGop = 20;
	private const long PtsPeriod = 1L << 33;

	private static readonly TimeSpan FrameDuration = TimeSpan.FromMilliseconds(FrameInterval);

	[Test]
	public async Task GopBurstOnConnectingCatchesUpWithTheLatestPictureAsync(CancellationToken cancellationToken)
	{
		SplitRecording recording = new();
		List<long> times = [];

		await recording.RunAsync(new FlvSplitter(), new RecordingSplit.None(), async session =>
		{
			await session.WriteAsync(FlvHead());
			times.Add(session.Now);

			// CDN 缓存的 GOP：0.5 秒内收到 6 秒画面
			for (int i = 0; i < 5; ++i)
			{
				session.Advance(TimeSpan.FromMilliseconds(100));
				await session.WriteAsync(FlvFrames(100_000 + (uint)i * 1200, 12, i is 0));
			}

			times.Add(session.Now);
			session.Advance(TimeSpan.FromMilliseconds(300));
			times.Add(session.Now);

			for (int i = 0; i < 2; ++i)
			{
				session.Advance(TimeSpan.FromMilliseconds(700 + 300 * i));
				await session.WriteAsync(FlvFrames(106_000 + (uint)i * 1000, 10, false));
				times.Add(session.Now);
			}

			session.Advance(TimeSpan.FromMilliseconds(500));
			times.Add(session.Now);
		}, cancellationToken);

		// 按文件建好时的墙钟算的话，0.5 秒时的弹幕会比画面早 5 秒多
		await Assert.That(Place(recording.Timeline, times)).IsEquivalentTo(Positions(0, 0, 5900, 6200, 6900, 7900, 8400), CollectionOrdering.Matching);
	}

	[Test]
	public async Task Fmp4BacklogFromTheOldestSegmentCatchesUpAsync(CancellationToken cancellationToken)
	{
		SplitRecording recording = new();
		List<long> times = [];

		await recording.RunAsync(new Fmp4HlsSplitter(), new RecordingSplit.None(), async session =>
		{
			await session.InitAsync(Fmp4Samples.TimedInit(1000, 1920, 1080, 40));

			// 播放列表里 6 个 1 秒的分片一下拉完，之后每秒一个
			for (int i = 0; i < 6; ++i)
			{
				await session.SegmentAsync(Fmp4Samples.TimedFragment(500_000 + (ulong)i * 1000, 25, 0));
				session.Advance(TimeSpan.FromMilliseconds(20));
			}

			times.Add(session.Now);
			session.Advance(TimeSpan.FromMilliseconds(500));
			times.Add(session.Now);
			session.Advance(TimeSpan.FromMilliseconds(500));
			await session.SegmentAsync(Fmp4Samples.TimedFragment(506_000, 25, 0));
			times.Add(session.Now);
		}, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(Place(recording.Timeline, times)).IsEquivalentTo(Positions(0, 6020, 6520, 7000), CollectionOrdering.Matching);
			await Assert.That(recording.Timeline.GetResolution("0")).IsEqualTo(new VideoResolution(1920, 1080));
		}
	}

	[Test]
	public async Task TsBacklogUsesTheLastPictureOfEachSegmentAsync(CancellationToken cancellationToken)
	{
		SplitRecording recording = new();
		List<long> times = [];

		await recording.RunAsync(new TsHlsSplitter(), new RecordingSplit.None(), async session =>
		{
			// 3 个 4 秒的分片，每秒一帧
			for (int i = 0; i < 3; ++i)
			{
				await session.SegmentAsync(TsSegment(900_000 + i * 360_000, 4, MediaHeaderTest.Avc1080));
				session.Advance(TimeSpan.FromMilliseconds(50));
			}

			times.Add(session.Now);
			session.Advance(TimeSpan.FromSeconds(2));
			times.Add(session.Now);
			session.Advance(TimeSpan.FromSeconds(2));
			await session.SegmentAsync(TsSegment(900_000 + 3 * 360_000, 4, null));
			times.Add(session.Now);
		}, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(Place(recording.Timeline, times)).IsEquivalentTo(Positions(0, 11_050, 13_050, 15_000), CollectionOrdering.Matching);
			await Assert.That(recording.Timeline.GetResolution("0")).IsEqualTo(new VideoResolution(1920, 1080));
		}
	}

	[Test]
	public async Task StalledStreamStopsMovingAfterTheLimitFromTheLastDataAsync(CancellationToken cancellationToken)
	{
		SplitRecording recording = new();
		List<long> times = [];
		byte[] large = LargeVideoTag();

		await recording.RunAsync(new FlvSplitter(), new RecordingSplit.None(), async session =>
		{
			await session.WriteAsync([.. FlvHead(), .. FlvFrames(100_000, 10, true)]);
			session.Advance(TimeSpan.FromSeconds(30));
			times.Add(session.Now);

			// 一个大的 tag 分 20 次、每秒到一点
			for (int i = 0; i < 20; ++i)
			{
				session.Advance(TimeSpan.FromSeconds(1));
				await session.WriteAsync(large.AsSpan(i * 100, i is 19 ? large.Length - 1900 : 100).ToArray());
			}

			times.Add(session.Now);
			session.Advance(TimeSpan.FromSeconds(30));
			times.Add(session.Now);
		}, cancellationToken);

		// 卡了 30 秒只往后推 10 秒；事后才查也按当时的情况算，不受之后到达的数据影响
		await Assert.That(Place(recording.Timeline, times)).IsEquivalentTo(Positions(0, 10_900, 1000, 11_000), CollectionOrdering.Matching);
	}

	// HLS 的空闲超时按分片间隔放宽，可能长于 10 秒；卡住期间内置重试成功后画面接着写进同一个文件
	[Test]
	[Arguments(3, 10_000)]
	[Arguments(20, 20_000)]
	public async Task StalledStreamMovesOnUntilTheIdleTimeoutAsync(int idleTimeoutSeconds, long expectedMilliseconds)
	{
		FakeTimeProvider time = new();
		RecordingTimeline timeline = new("0", time, TimeSpan.FromSeconds(idleTimeoutSeconds));
		timeline.AddMedia([new MediaMark(1, MediaSpan.At(TimeSpan.FromSeconds(10)), null)], time.GetTimestamp());
		timeline.Commit(1);
		time.Advance(TimeSpan.FromSeconds(30));
		long stalled = time.GetTimestamp();
		timeline.EndReceiving();
		timeline.Close();

		await Assert.That(Place(timeline, [stalled])).IsEquivalentTo(Positions(0, expectedMilliseconds), CollectionOrdering.Matching);
	}

	[Test]
	public async Task DataWithoutCompleteElementsKeepsTheClockRunningAsync(CancellationToken cancellationToken)
	{
		SplitRecording recording = new();
		List<long> times = [];
		byte[] large = LargeVideoTag();

		await recording.RunAsync(new FlvSplitter(), new RecordingSplit.None(), async session =>
		{
			await session.WriteAsync([.. FlvHead(), .. FlvFrames(100_000, 10, true)]);

			for (int i = 0; i < 15; ++i)
			{
				session.Advance(TimeSpan.FromSeconds(1));
				await session.WriteAsync(large.AsSpan(i * 100, 100).ToArray());
			}

			times.Add(session.Now);
			session.Advance(TimeSpan.FromSeconds(30));
			times.Add(session.Now);
		}, cancellationToken);

		await Assert.That(Place(recording.Timeline, times)).IsEquivalentTo(Positions(0, 15_900, 25_900), CollectionOrdering.Matching);
	}

	[Test]
	[Arguments(SplitKind.Duration)]
	[Arguments(SplitKind.Size)]
	[Arguments(SplitKind.Title)]
	public async Task EachFileStartsAtZeroFromItsFirstKeyFrameAsync(SplitKind kind, CancellationToken cancellationToken)
	{
		// 第 40 帧是第三个关键帧；前两个 GOP 加上流头正好放得下，第三个 GOP 放不下
		int firstFileLength = FlvHead().Length + FlvVideo(0, 2 * FramesPerGop).Length;
		(SplitControl control, SplitReason reason) = kind switch
		{
			SplitKind.Duration => (SplitRecording.Fixed(new RecordingSplit.ByDuration(TimeSpan.FromSeconds(3))), SplitReason.Duration),
			SplitKind.Size => (SplitRecording.Fixed(new RecordingSplit.BySize(firstFileLength + 1)), SplitReason.Size),
			_ => (SplitRecording.Fixed(new RecordingSplit.None(), true), SplitReason.Title)
		};
		SplitRecording recording = new();
		List<long> times = [];
		TimelinePosition? beforeBoundary = null;

		await recording.RunAsync(new FlvSplitter(), control, async session =>
		{
			await session.WriteAsync(FlvHead());

			for (int frame = 0; frame <= 65; ++frame)
			{
				if (frame is 2 * FramesPerGop && kind is SplitKind.Title)
				{
					control.RequestTitleSplit();
				}

				await session.WriteAsync(FlvFrame(10_000 + (uint)frame * FrameInterval, frame % FramesPerGop is 0));
				session.Advance(TimeSpan.FromMilliseconds(50));
				times.Add(session.Now);

				if (frame is 45)
				{
					// 第三个 GOP 写进哪个文件要等下一个关键帧到达才知道；之前的弹幕已经确定
					Task<TimelinePosition?> deferred = recording.Timeline.PlaceAsync(session.Now, cancellationToken).AsTask();
					await Assert.That(recording.Timeline.TryPlace(times[39], out beforeBoundary)).IsTrue();
					await Assert.That(deferred.IsCompleted).IsFalse();
					session.Advance(TimeSpan.FromMilliseconds(50));
					await WriteFramesAsync(session, 46, 59, times);
					await Assert.That(deferred.IsCompleted).IsFalse();
					await session.WriteAsync(FlvFrame(10_000 + 60 * FrameInterval, true));
					await Assert.That(await deferred).IsEqualTo(At(1, 550));
					frame = 60;
					session.Advance(TimeSpan.FromMilliseconds(50));
					times.Add(session.Now);
				}

				session.Advance(TimeSpan.FromMilliseconds(50));
			}
		}, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(recording.Files).Count().IsEqualTo(2);
			await Assert.That(recording.Starts.Select(static start => start.Reason)).IsEquivalentTo([reason]);
			await Assert.That(beforeBoundary).IsEqualTo(At(0, 3950));
			await Assert.That(FlvSamples.Describe(recording.Files[1]).First(static tag => tag.EndsWith("[k]", StringComparison.Ordinal))).IsEqualTo("V0[k]");
			await Assert.That(Place(recording.Timeline, [times[39], times[40], times[^1]])).IsEquivalentTo([At(0, 3950), At(1, 50), At(1, 2550)], CollectionOrdering.Matching);
		}
	}

	[Test]
	public async Task SegmentIsSettledBeforeItsFileIsReportedCompleteAsync(CancellationToken cancellationToken)
	{
		SplitRecording recording = new();
		long beforeBoundary = recording.Time.GetTimestamp() + 395 * recording.Time.TimestampFrequency / 100;
		bool? settled = null;
		TimelinePosition? last = null;

		// 下一段从第 40 帧开始，它之前一刻收到的弹幕这时已经确定
		recording.SegmentCompleted = _ => settled = recording.Timeline.TryPlace(beforeBoundary, out last);

		await recording.RunAsync(new FlvSplitter(), new RecordingSplit.ByDuration(TimeSpan.FromSeconds(3)), async session =>
		{
			await session.WriteAsync(FlvHead());
			await WriteFramesAsync(session, 0, 70, []);
		}, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(settled).IsTrue();
			await Assert.That(last).IsEqualTo(At(0, 3950));
		}
	}

	[Test]
	public async Task FlvTimelineRestartContinuesAndForwardJumpsAreFollowedAsync(CancellationToken cancellationToken)
	{
		SplitRecording recording = new();
		List<long> times = [];

		await recording.RunAsync(new FlvSplitter(), new RecordingSplit.None(), async session =>
		{
			await session.WriteAsync(FlvHead());

			foreach (uint timestamp in (uint[])[10_000, 10_500, 11_000, 0, 500, 3_600_500])
			{
				await session.WriteAsync(FlvFrame(timestamp, timestamp is 10_000 or 0));
				times.Add(session.Now);
				session.Advance(TimeSpan.FromMilliseconds(500));
			}
		}, cancellationToken);

		await Assert.That(Place(recording.Timeline, times)).IsEquivalentTo(Positions(0, 0, 500, 1000, 1000, 1500, 3_601_500), CollectionOrdering.Matching);
	}

	[Test]
	public async Task TsPtsWrapKeepsTheFileTimeGoingAsync(CancellationToken cancellationToken)
	{
		SplitRecording recording = new();
		List<long> times = [];

		// 开着分段时单元要等下一个关键帧，这里一直等到收流结束才提交
		await recording.RunAsync(new TransportStreamSplitter(), new RecordingSplit.ByDuration(TimeSpan.FromHours(1)), async session =>
		{
			await session.WriteAsync([.. TsSamples.Pat(), .. TsSamples.Pmt()]);

			foreach (long pts in (long[])[PtsPeriod - 180_000, PtsPeriod - 90_000, 0, 90_000])
			{
				bool isFirst = pts is PtsPeriod - 180_000;
				await session.WriteAsync(TsSamples.TimedVideoFrame(isFirst ? TsSamples.AvcIdr : TsSamples.AvcSlice, pts, isFirst, isFirst ? Convert.FromHexString(MediaHeaderTest.Avc480) : null));
				times.Add(session.Now);
				session.Advance(TimeSpan.FromSeconds(1));
			}
		}, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(Place(recording.Timeline, times)).IsEquivalentTo(Positions(0, 0, 1000, 2000, 3000), CollectionOrdering.Matching);
			await Assert.That(recording.Timeline.GetResolution("0")).IsEqualTo(new VideoResolution(854, 480));
		}
	}

	[Test]
	public async Task TsPtsResetWithinAnEraContinuesFromTheLatestPictureAsync(CancellationToken cancellationToken)
	{
		SplitRecording recording = new();
		List<long> times = [];

		await recording.RunAsync(new TsHlsSplitter(), new RecordingSplit.None(), async session =>
		{
			await session.SegmentAsync(TsSegment(900_000, 4, null));
			times.Add(session.Now);
			session.Advance(TimeSpan.FromSeconds(4));
			await session.SegmentAsync(TsSegment(0, 4, null));
			times.Add(session.Now);
		}, cancellationToken);

		await Assert.That(Place(recording.Timeline, times)).IsEquivalentTo(Positions(0, 3000, 6000), CollectionOrdering.Matching);
	}

	// HLS 换 era 时会话以 Discontinuity 结束，下一次尝试是新的文件和新的时间轴
	[Test]
	public async Task NothingIsPlacedOutsideTheAttemptAsync(CancellationToken cancellationToken)
	{
		SplitRecording recording = new();
		long before = recording.Time.GetTimestamp() - 1;
		long during = 0;

		await recording.RunAsync(new TsHlsSplitter(), new RecordingSplit.None(), async session =>
		{
			await session.SegmentAsync(TsSegment(900_000, 4, null));
			session.Advance(TimeSpan.FromSeconds(1));
			during = session.Now;
			session.Advance(TimeSpan.FromSeconds(1));
		}, cancellationToken);

		recording.Time.Advance(TimeSpan.FromMilliseconds(1));

		using (Assert.Multiple())
		{
			await Assert.That(Place(recording.Timeline, [before, during, recording.Time.GetTimestamp()])).IsEquivalentTo([null, At(0, 4000), null], CollectionOrdering.Matching);
			await Assert.That(await recording.Timeline.PlaceAsync(recording.Time.GetTimestamp(), cancellationToken)).IsNull();
		}
	}

	[Test]
	public async Task WithoutMediaTimesEachFileCountsFromItsFirstUnitAsync(CancellationToken cancellationToken)
	{
		SplitRecording recording = new();
		List<long> times = [];

		await recording.RunAsync(new FakeSplitter(), new RecordingSplit.ByDuration(TimeSpan.FromMinutes(1)), async session =>
		{
			session.Advance(TimeSpan.FromSeconds(2));
			await session.WriteAsync(FakeSplitter.Unit("a", 10));
			session.Advance(TimeSpan.FromSeconds(1));
			times.Add(session.Now);
			session.Advance(TimeSpan.FromMinutes(1));
			await session.WriteAsync(FakeSplitter.Unit("b", 10));
			session.Advance(TimeSpan.FromSeconds(1));
			times.Add(session.Now);
			await session.WriteAsync(FakeSplitter.Unit("c", 10));
		}, cancellationToken);

		// 第一个文件从建好时算，之后的文件从它第一个单元到达时算
		await Assert.That(Place(recording.Timeline, times)).IsEquivalentTo([At(0, 3000), At(1, 1000)], CollectionOrdering.Matching);
	}

	[Test]
	public async Task EachFileKeepsTheSizeFromItsOwnHeaderAsync(CancellationToken cancellationToken)
	{
		SplitRecording recording = new();

		await recording.RunAsync(new FlvSplitter(), new RecordingSplit.ByDuration(TimeSpan.FromSeconds(3)), async session =>
		{
			await session.WriteAsync(FlvHead());

			for (int frame = 0; frame < 70; ++frame)
			{
				// 换分辨率时编码器先发新的序列头再发关键帧
				if (frame is 2 * FramesPerGop)
				{
					await session.WriteAsync(FlvSamples.AvcSequenceHeader(Convert.FromHexString(MediaHeaderTest.Avc444720), 14_000));
				}

				await session.WriteAsync(FlvFrame(10_000 + (uint)frame * FrameInterval, frame % FramesPerGop is 0));
				session.Advance(FrameDuration);
			}
		}, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(recording.Files).Count().IsEqualTo(2);
			await Assert.That(recording.Timeline.GetResolution("0")).IsEqualTo(new VideoResolution(1920, 1080));
			await Assert.That(recording.Timeline.GetResolution("1")).IsEqualTo(new VideoResolution(1280, 720));
			await Assert.That(recording.Timeline.GetResolution("2")).IsNull();
		}
	}

	// 时间戳本来就从 0 开始，改写不动字节
	[Test]
	public async Task ReadingTheTimelineLeavesTheRecordedBytesUnchangedAsync(CancellationToken cancellationToken)
	{
		byte[] flv = [.. FlvHead(), .. Enumerable.Range(0, 50).SelectMany(static frame => (byte[])[.. FlvFrame((uint)frame * FrameInterval, frame % FramesPerGop is 0), .. FlvSamples.Audio((uint)frame * FrameInterval + 20)])];
		byte[] init = Fmp4Samples.TimedInit(90_000, 1920, 1080, 3000);
		byte[][] fragments = [Fmp4Samples.TimedFragment(0, 30, 3000), Fmp4Samples.TimedFragment(90_000, 30, 0, false), Fmp4Samples.TimedFragment(180_000, 30, 3000)];
		byte[][] tsSegments = [TsSegment(0, 4, MediaHeaderTest.Avc1080), TsSegment(360_000, 4, null)];
		byte[] ts = [.. TsSamples.Pat(), .. TsSamples.Pmt(), .. TsSamples.TimedVideoFrame(TsSamples.AvcIdr, 0, true, Convert.FromHexString(MediaHeaderTest.Avc480)), .. TsSamples.Audio(), .. TsSamples.TimedVideoFrame(TsSamples.AvcSlice, 3000)];
		SplitRecording flvRecording = new();
		SplitRecording fmp4Recording = new();
		SplitRecording tsHlsRecording = new();
		SplitRecording tsRecording = new();

		await flvRecording.RunAsync(new FlvSplitter(), new RecordingSplit.None(), session => session.WriteInChunksAsync(flv, 7), cancellationToken);
		await fmp4Recording.RunAsync(new Fmp4HlsSplitter(), new RecordingSplit.None(), async session =>
		{
			await session.InitAsync(init);

			foreach (byte[] fragment in fragments)
			{
				await session.SegmentAsync(fragment);
			}
		}, cancellationToken);
		await tsHlsRecording.RunAsync(new TsHlsSplitter(), new RecordingSplit.None(), async session =>
		{
			foreach (byte[] segment in tsSegments)
			{
				await session.SegmentAsync(segment);
			}
		}, cancellationToken);
		await tsRecording.RunAsync(new TransportStreamSplitter(), new RecordingSplit.None(), session => session.WriteInChunksAsync(ts, 100), cancellationToken);

		byte[] fmp4 = [.. init, .. fragments.SelectMany(static fragment => fragment)];
		byte[] tsHls = [.. tsSegments.SelectMany(static segment => segment)];
		byte[] flvFile = await Assert.That(flvRecording.Files).HasSingleItem();
		byte[] fmp4File = await Assert.That(fmp4Recording.Files).HasSingleItem();
		byte[] tsHlsFile = await Assert.That(tsHlsRecording.Files).HasSingleItem();
		byte[] tsFile = await Assert.That(tsRecording.Files).HasSingleItem();

		using (Assert.Multiple())
		{
			await Assert.That((flvFile.Length, flvFile.AsSpan().CommonPrefixLength(flv))).IsEqualTo((flv.Length, flv.Length));
			await Assert.That((fmp4File.Length, fmp4File.AsSpan().CommonPrefixLength(fmp4))).IsEqualTo((fmp4.Length, fmp4.Length));
			await Assert.That((tsHlsFile.Length, tsHlsFile.AsSpan().CommonPrefixLength(tsHls))).IsEqualTo((tsHls.Length, tsHls.Length));
			await Assert.That((tsFile.Length, tsFile.AsSpan().CommonPrefixLength(ts))).IsEqualTo((ts.Length, ts.Length));
		}
	}

	[Test]
	public async Task SplittingWithTheTimelineWritesTheSameFilesAsync(CancellationToken cancellationToken)
	{
		byte[] head = FlvHead();

		// 源时间戳从 10 秒开始，第一个文件同样从第一帧的 0 开始
		byte[] expectedFirst = [.. head, .. Enumerable.Range(0, 2 * FramesPerGop).SelectMany(static frame => FlvFrame((uint)frame * FrameInterval, frame % FramesPerGop is 0))];
		SplitRecording recording = new();

		await recording.RunAsync(new FlvSplitter(), new RecordingSplit.ByDuration(TimeSpan.FromSeconds(3)), async session =>
		{
			await session.WriteAsync(head);

			for (int frame = 0; frame < 70; ++frame)
			{
				await session.WriteAsync(FlvFrame(10_000 + (uint)frame * FrameInterval, frame % FramesPerGop is 0));
				session.Advance(FrameDuration);
			}
		}, cancellationToken);

		byte[] first = recording.Files[0];

		using (Assert.Multiple())
		{
			await Assert.That((first.Length, first.AsSpan().CommonPrefixLength(expectedFirst))).IsEqualTo((expectedFirst.Length, expectedFirst.Length));
			await Assert.That(FlvSamples.Describe(recording.Files[1]).Skip(3).Take(3)).IsEquivalentTo(["V0[k]", "V100[p]", "V200[p]"], CollectionOrdering.Matching);
		}
	}

	// 保留 1 秒、攒够 4 条才删；早于保留范围的弹幕按最早留下的记录放
	[Test]
	public async Task PrunedRecordsKeepLaterPositionsAndBoundTheRecordCountAsync()
	{
		FakeTimeProvider time = new();
		RecordingTimeline timeline = new("0", time, RoomDefaults.StreamIdleTimeout, TimeSpan.FromSeconds(1), 4);
		long[] times = new long[40];
		TimelinePosition? beforePruning = null;

		for (int frame = 0; frame < times.Length; ++frame)
		{
			timeline.AddMedia([new MediaMark(frame + 1, MediaSpan.At(TimeSpan.FromMilliseconds(10_000 + frame * 100)), null)], time.GetTimestamp());

			if (frame is 20)
			{
				timeline.OpenSegment(timeline.StartSegment(), "1");
			}

			timeline.Commit(frame + 1);
			time.Advance(TimeSpan.FromMilliseconds(50));
			times[frame] = time.GetTimestamp();
			time.Advance(TimeSpan.FromMilliseconds(50));

			if (frame is 12)
			{
				timeline.TryPlace(times[5], out beforePruning);
			}
		}

		timeline.EndReceiving();
		timeline.Close();

		using (Assert.Multiple())
		{
			await Assert.That(beforePruning).IsEqualTo(At(0, 550));
			await Assert.That(Place(timeline, [times[5], times[19], times[29], times[35], times[39]])).IsEquivalentTo([At(1, 800), At(1, 800), At(1, 950), At(1, 1550), At(1, 1950)], CollectionOrdering.Matching);

			// 保留范围内的 11 条，加上攒着还没删的最多 3 条
			await Assert.That(timeline.RecordCount).IsLessThanOrEqualTo(14);
		}
	}

	// 攒着的单元中间卡流，卡得比保留时长还久，空闲超时还没到
	[Test]
	public async Task StallLongerThanTheRetentionKeepsTheRecordsItsDanmakuAreWaitingForAsync(CancellationToken cancellationToken)
	{
		FakeTimeProvider time = new();
		RecordingTimeline timeline = new("0", time, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(1), 4);
		long inHeldUnit = 0;

		for (int frame = 0; frame < 25; ++frame)
		{
			AddFrame(frame);

			// 最后 5 帧攒着，等下一个关键帧才提交
			if (frame < 20)
			{
				timeline.Commit(frame + 1);
			}

			time.Advance(TimeSpan.FromMilliseconds(50));

			if (frame is 22)
			{
				inHeldUnit = time.GetTimestamp();
			}

			time.Advance(TimeSpan.FromMilliseconds(50));
		}

		time.Advance(TimeSpan.FromMilliseconds(2500));
		long earlyInStall = time.GetTimestamp();
		time.Advance(TimeSpan.FromMilliseconds(2500));
		long lateInStall = time.GetTimestamp();
		Task<TimelinePosition?> placingInHeldUnit = timeline.PlaceAsync(inHeldUnit, cancellationToken).AsTask();
		Task<TimelinePosition?> placingLateInStall = timeline.PlaceAsync(lateInStall, cancellationToken).AsTask();
		bool waited = !placingInHeldUnit.IsCompleted && !placingLateInStall.IsCompleted;

		// 恢复后先到的是同一个单元剩下的帧
		time.Advance(TimeSpan.FromSeconds(1));
		AddFrame(25);
		timeline.Commit(26);
		TimelinePosition?[] woken = [await placingInHeldUnit, await placingLateInStall];

		// 之后的提交再剪除时，还在排队的卡流期间的弹幕仍然从卡流前最后一帧往后推
		time.Advance(TimeSpan.FromMilliseconds(100));
		AddFrame(26);
		timeline.Commit(27);

		using (Assert.Multiple())
		{
			await Assert.That(waited).IsTrue();
			await Assert.That(woken).IsEquivalentTo(Positions(0, 2250, 7500), CollectionOrdering.Matching);
			await Assert.That(Place(timeline, [earlyInStall, lateInStall])).IsEquivalentTo(Positions(0, 5000, 7500), CollectionOrdering.Matching);
		}

		void AddFrame(int frame)
		{
			timeline.AddMedia([new MediaMark(frame + 1, MediaSpan.At(TimeSpan.FromMilliseconds(10_000 + frame * 100)), null)], time.GetTimestamp());
		}
	}

	private static async Task WriteFramesAsync(SessionWriter session, int first, int last, List<long> times)
	{
		for (int frame = first; frame <= last; ++frame)
		{
			await session.WriteAsync(FlvFrame(10_000 + (uint)frame * FrameInterval, frame % FramesPerGop is 0));
			session.Advance(TimeSpan.FromMilliseconds(50));
			times.Add(session.Now);
			session.Advance(TimeSpan.FromMilliseconds(50));
		}
	}

	private static byte[] FlvHead()
	{
		return [.. FlvSamples.Header, .. FlvSamples.MetadataWithSize(1920, 1080), .. FlvSamples.AvcSequenceHeader(Convert.FromHexString(MediaHeaderTest.Avc1080)), .. FlvSamples.AudioSequenceHeader()];
	}

	private static byte[] LargeVideoTag()
	{
		return FlvSamples.Tag(FlvSamples.VideoTag, 101_000, [0x27, 1, 0, 0, 0, .. new byte[2000]]);
	}

	private static byte[] FlvVideo(int first, int count)
	{
		return [.. Enumerable.Range(first, count).SelectMany(static frame => FlvFrame(10_000 + (uint)frame * FrameInterval, frame % FramesPerGop is 0))];
	}

	private static byte[] FlvFrame(uint timestamp, bool isKeyFrame)
	{
		return FlvSamples.Video(VideoStyle.Avc, isKeyFrame ? VideoPacket.KeyFrame : VideoPacket.InterFrame, timestamp, isKeyFrame ? "k" : "p");
	}

	private static byte[] FlvFrames(uint start, int count, bool startsWithKeyFrame)
	{
		return
		[
			.. Enumerable.Range(0, count).SelectMany(index =>
			{
				uint timestamp = start + (uint)index * FrameInterval;
				return (byte[])[.. FlvFrame(timestamp, index is 0 && startsWithKeyFrame), .. FlvSamples.Audio(timestamp - 20)];
			})
		];
	}

	private static byte[] TsSegment(long startPts, int frames, string? sps)
	{
		return
		[
			.. TsSamples.Pat(), .. TsSamples.Pmt(),
			.. Enumerable.Range(0, frames).SelectMany(index => TsSamples.TimedVideoFrame(index is 0 ? TsSamples.AvcIdr : TsSamples.AvcSlice, startPts + index * 90_000L, index is 0, index is 0 && sps is not null ? Convert.FromHexString(sps) : null))
		];
	}

	private static TimelinePosition?[] Place(RecordingTimeline timeline, IEnumerable<long> times)
	{
		return [.. times.Select(time => timeline.TryPlace(time, out TimelinePosition? position) ? position : throw new InvalidOperationException("The position is not settled after the recording."))];
	}

	private static TimelinePosition?[] Positions(int segment, params long[] milliseconds)
	{
		return [.. milliseconds.Select(value => At(segment, value))];
	}

	// 按整毫秒构造：浮点秒数换成 tick 时会截掉 1
	private static TimelinePosition? At(int segment, long milliseconds)
	{
		return new TimelinePosition(segment, segment.ToString(CultureInfo.InvariantCulture), TimeSpan.FromTicks(milliseconds * TimeSpan.TicksPerMillisecond));
	}
}

public enum SplitKind
{
	Duration,
	Size,
	Title
}
