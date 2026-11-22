using BilibiliLive.Danmu;
using BilibiliLiveRecordDownLoader.Application.Contracts.Media;
using BilibiliLiveRecordDownLoader.Application.Recording;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class RoomSplitRecordingTest
{
	private const int FrameSize = 40 << 10;

	[Test]
	[Timeout(30_000)]
	[Arguments(true, null, 2)]
	[Arguments(true, false, 1)]
	[Arguments(false, true, 2)]
	[Arguments(false, null, 1)]
	public async Task TitleChangeWhileRecordingStartsANewFileAtTheNextKeyFrameAsync(bool global, bool? room, int expectedFiles, CancellationToken cancellationToken)
	{
		RecordingMediaTools mediaTools = new();
		await using RecordingLoopHarness loop = Create(cancellationToken, mediaTools);
		loop.Config.SplitMode = RecordingSplitMode.None;

		loop.GoLive();
		StreamRequest stream = await loop.NextStreamAsync();
		await stream.AcceptAsync([.. FlvSamples.Header, .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.SequenceHeader, 0), .. Frame(VideoPacket.KeyFrame, 0)]);
		await stream.SendAndWaitAsync(Frame(VideoPacket.InterFrame, 33));

		// 开始录制之后才打开的设置同样生效
		loop.Config.SplitOnTitleChange = global;
		loop.Settings.SplitOnTitleChange = room;
		loop.Session.Handle(new DanmuMessage.RoomChanged("new title", null, null));
		await stream.SendAndWaitAsync(Frame(VideoPacket.KeyFrame, 2000));
		await stream.SendAndWaitAsync(Frame(VideoPacket.InterFrame, 2033));
		await stream.SendAndWaitAsync(Frame(VideoPacket.KeyFrame, 4000));
		await stream.EndAsync();

		// 同一秒内的第二段文件名带序号
		await Assert.That(await RecordedFilesAfterTheAttemptAsync(loop)).IsEqualTo(expectedFiles is 2 ? "20260101_000000.flv, 20260101_000000_1.flv" : "20260101_000000.flv");
	}

	[Test]
	[Timeout(30_000)]
	public async Task TitleChangeBeforeRecordingStartsDoesNotSplitAsync(CancellationToken cancellationToken)
	{
		RecordingMediaTools mediaTools = new();
		await using RecordingLoopHarness loop = Create(cancellationToken, mediaTools);
		loop.Config.SplitMode = RecordingSplitMode.None;
		loop.Config.SplitOnTitleChange = true;

		loop.GoLive();
		StreamRequest stream = await loop.NextStreamAsync();
		loop.Session.Handle(new DanmuMessage.RoomChanged("new title", null, null));
		await stream.AcceptAsync([.. FlvSamples.Header, .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.SequenceHeader, 0), .. Frame(VideoPacket.KeyFrame, 0)]);
		await stream.SendAndWaitAsync(Frame(VideoPacket.InterFrame, 33));
		await stream.SendAndWaitAsync(Frame(VideoPacket.KeyFrame, 2000));
		await stream.EndAsync();

		await Assert.That(await RecordedFilesAfterTheAttemptAsync(loop)).IsEqualTo("20260101_000000.flv");
	}

	[Test]
	[Timeout(30_000)]
	public async Task FileStartedAfterATitleChangeIsNamedWithTheNewTitleAsync(CancellationToken cancellationToken)
	{
		RecordingMediaTools mediaTools = new();
		await using RecordingLoopHarness loop = Create(cancellationToken, mediaTools);
		loop.Config.SplitMode = RecordingSplitMode.None;
		loop.Config.SplitOnTitleChange = true;
		loop.Config.IncludeTitleInFileName = true;

		loop.GoLive();
		StreamRequest stream = await loop.NextStreamAsync();
		await stream.AcceptAsync([.. FlvSamples.Header, .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.SequenceHeader, 0), .. Frame(VideoPacket.KeyFrame, 0)]);
		await stream.SendAndWaitAsync(Frame(VideoPacket.InterFrame, 33));
		loop.Session.Handle(new DanmuMessage.RoomChanged("updated title", null, null));
		await stream.SendAndWaitAsync(Frame(VideoPacket.KeyFrame, 2000));
		await stream.SendAndWaitAsync(Frame(VideoPacket.KeyFrame, 4000));
		await stream.EndAsync();

		await Assert.That(await RecordedFilesAfterTheAttemptAsync(loop)).IsEqualTo("20260101_000000_title.flv, 20260101_000000_updated title.flv");
	}

	[Test]
	[Timeout(30_000)]
	public async Task TitleInFileNameFollowsTheSettingWhenEachFileStartsAsync(CancellationToken cancellationToken)
	{
		RecordingMediaTools mediaTools = new();
		await using RecordingLoopHarness loop = Create(cancellationToken, mediaTools);
		loop.Config.IncludeTitleInFileName = true;

		loop.GoLive();
		StreamRequest stream = await loop.NextStreamAsync();
		await stream.AcceptAsync([.. FlvSamples.Header, .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.SequenceHeader, 0), .. Frame(VideoPacket.KeyFrame, 0)]);
		await stream.SendAndWaitAsync(Frame(VideoPacket.InterFrame, 33));
		loop.Config.IncludeTitleInFileName = false;
		loop.Time.Advance(TimeSpan.FromSeconds(61));
		await stream.SendAndWaitAsync(Frame(VideoPacket.KeyFrame, 61_000));
		await stream.SendAndWaitAsync(Frame(VideoPacket.KeyFrame, 63_000));
		await stream.EndAsync();

		await Assert.That(await RecordedFilesAfterTheAttemptAsync(loop)).IsEqualTo("20260101_000000_title.flv, 20260101_000101.flv");
	}

	[Test]
	[Timeout(30_000)]
	public async Task EachFileIsPostProcessedOnceFinishedWhileTheRecordingTimeKeepsRunningAsync(CancellationToken cancellationToken)
	{
		RecordingMediaTools mediaTools = new();
		TaskCompletionSource nextFileWritten = new(TaskCreationOptions.RunContinuationsAsynchronously);
		int files = 0;
		await using RecordingLoopHarness loop = Create(cancellationToken, mediaTools, path => Interlocked.Increment(ref files) is 1 ? LiveRecorder.CreateRecordingFile(path) : new FirstWriteFile(path, nextFileWritten));

		loop.GoLive();
		StreamRequest stream = await loop.NextStreamAsync();
		await stream.AcceptAsync([.. FlvSamples.Header, .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.SequenceHeader, 0), .. Frame(VideoPacket.KeyFrame, 0)]);
		await stream.SendAndWaitAsync(Frame(VideoPacket.InterFrame, 33));
		ActiveRecordingDto? first = loop.Snapshot.Recording;

		loop.Time.Advance(TimeSpan.FromSeconds(61));
		await stream.SendAndWaitAsync(Frame(VideoPacket.KeyFrame, 61_000));
		await stream.SendAndWaitAsync(Frame(VideoPacket.InterFrame, 61_033));
		await stream.SendAndWaitAsync(Frame(VideoPacket.KeyFrame, 63_000));
		RemuxInput finished = await WaitForRemuxAsync(mediaTools, 1);

		// 写盘端换完文件才往新文件里写，这时新的一段已经开始
		await nextFileWritten.Task.WaitAsync(cancellationToken);
		ActiveRecordingDto? second = loop.Snapshot.Recording;

		await stream.EndAsync();
		RemuxInput last = await WaitForRemuxAsync(mediaTools, 2);
		string[] recorded = Directory.GetFiles(RecordingFileLayout.GetRoomDirectory(loop.Config.RecordDirectory, RecordingLoopHarness.RoomId), "*.flv");

		using (Assert.Multiple())
		{
			await Assert.That(first).IsNotNull();
			await Assert.That(second).IsEqualTo(first);
			await Assert.That(recorded.Length).IsEqualTo(2);
			await Assert.That(finished.Source).IsNotEqualTo(last.Source);
			await Assert.That(recorded.Order(StringComparer.Ordinal).SequenceEqual([finished.Source, last.Source], StringComparer.Ordinal)).IsTrue();
			await Assert.That(Path.GetFileNameWithoutExtension(last.Source)).IsEqualTo("20260101_000101");
		}
	}

	[Test]
	[Timeout(30_000)]
	public async Task FileStillClosingWhenTheAttemptEndsIsPostProcessedOnlyOnceAndNothingFollowsAsync(CancellationToken cancellationToken)
	{
		RecordingMediaTools mediaTools = new();
		TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
		StuckClosingFile? first = null;
		await using RecordingLoopHarness loop = Create(cancellationToken, mediaTools, path => first is null ? first = new StuckClosingFile(path, release.Task) : LiveRecorder.CreateRecordingFile(path));

		loop.GoLive();
		StreamRequest stream = await loop.NextStreamAsync();
		await stream.AcceptAsync([.. FlvSamples.Header, .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.SequenceHeader, 0), .. Frame(VideoPacket.KeyFrame, 0)]);
		await stream.SendAndWaitAsync(Frame(VideoPacket.InterFrame, 33));
		loop.Time.Advance(TimeSpan.FromSeconds(61));
		await stream.SendAndWaitAsync(Frame(VideoPacket.KeyFrame, 61_000));
		await stream.SendAndWaitAsync(Frame(VideoPacket.KeyFrame, 63_000));

		// 换文件时关旧文件卡住，下播后排空超时，录制循环把旧文件交给后处理
		loop.GoOffline();
		await stream.EndAsync();
		RemuxInput finished = await WaitForRemuxAsync(mediaTools, 1);
		DateTimeOffset? startedAt = loop.Snapshot.Recording?.StartedAt;
		release.SetResult();
		await first!.Closed.WaitAsync(cancellationToken);
		await Task.Delay(200, cancellationToken);
		string[] recorded = Directory.GetFiles(RecordingFileLayout.GetRoomDirectory(loop.Config.RecordDirectory, RecordingLoopHarness.RoomId), "*.flv");

		using (Assert.Multiple())
		{
			await Assert.That(mediaTools.Remuxes.Count).IsEqualTo(1);
			await Assert.That(recorded.SequenceEqual([finished.Source], StringComparer.Ordinal)).IsTrue();
			await Assert.That(loop.Snapshot.Recording?.StartedAt).IsEqualTo(startedAt);
		}
	}

	[Test]
	[Timeout(30_000)]
	public async Task SplittingTurnedOnWhileRecordingSplitsTheCurrentRecordingAsync(CancellationToken cancellationToken)
	{
		RecordingMediaTools mediaTools = new();
		await using RecordingLoopHarness loop = Create(cancellationToken, mediaTools);
		loop.Config.SplitMode = RecordingSplitMode.None;

		loop.GoLive();
		StreamRequest stream = await loop.NextStreamAsync();
		await stream.AcceptAsync([.. FlvSamples.Header, .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.SequenceHeader, 0), .. Frame(VideoPacket.KeyFrame, 0)]);
		await stream.SendAndWaitAsync(Frame(VideoPacket.InterFrame, 33));
		loop.Config.SplitMode = RecordingSplitMode.Duration;
		loop.Time.Advance(TimeSpan.FromSeconds(61));
		await stream.SendAndWaitAsync(Frame(VideoPacket.KeyFrame, 61_000));
		await stream.SendAndWaitAsync(Frame(VideoPacket.InterFrame, 61_033));
		await stream.SendAndWaitAsync(Frame(VideoPacket.KeyFrame, 63_000));
		await stream.EndAsync();

		await Assert.That(await RecordedFilesAfterTheAttemptAsync(loop)).IsEqualTo("20260101_000000.flv, 20260101_000101.flv");
	}

	[Test]
	[Timeout(30_000)]
	public async Task ConversionFollowsTheSettingWhenEachFileIsFinishedAsync(CancellationToken cancellationToken)
	{
		RecordingMediaTools mediaTools = new();
		await using RecordingLoopHarness loop = Create(cancellationToken, mediaTools);
		loop.Config.ConvertToMp4 = false;

		loop.GoLive();
		StreamRequest stream = await loop.NextStreamAsync();
		await stream.AcceptAsync([.. FlvSamples.Header, .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.SequenceHeader, 0), .. Frame(VideoPacket.KeyFrame, 0)]);
		await stream.SendAndWaitAsync(Frame(VideoPacket.InterFrame, 33));
		loop.Time.Advance(TimeSpan.FromSeconds(61));
		await stream.SendAndWaitAsync(Frame(VideoPacket.KeyFrame, 61_000));
		await stream.SendAndWaitAsync(Frame(VideoPacket.KeyFrame, 63_000));

		// 新文件建好时旧文件已经按当时的设置交给了后处理
		await WaitForFilesAsync(loop, 2);
		loop.Config.ConvertToMp4 = true;
		await stream.EndAsync();
		RemuxInput converted = await WaitForRemuxAsync(mediaTools, 1);
		await Task.Delay(200, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(mediaTools.Remuxes.Count).IsEqualTo(1);
			await Assert.That(Path.GetFileNameWithoutExtension(converted.Source)).IsEqualTo("20260101_000101");
		}
	}

	// 重连时上一次尝试已经结束；文件都超过后处理的下限，不会被删掉
	private static async Task<string> RecordedFilesAfterTheAttemptAsync(RecordingLoopHarness loop)
	{
		_ = await loop.NextStreamAsync();
		string directory = RecordingFileLayout.GetRoomDirectory(loop.Config.RecordDirectory, RecordingLoopHarness.RoomId);
		return string.Join(", ", Directory.GetFiles(directory, "*.flv").Select(Path.GetFileName).Order(StringComparer.Ordinal));
	}

	private static RecordingLoopHarness Create(CancellationToken cancellationToken, RecordingMediaTools mediaTools, Func<string, Stream>? createFile = null)
	{
		RecordingLoopHarness loop = new(cancellationToken, createFile, mediaTools: mediaTools);
		loop.Time.SetUtcNow(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
		loop.Time.SetLocalTimeZone(TimeZoneInfo.Utc);
		loop.Config.SplitMode = RecordingSplitMode.Duration;
		loop.Config.SplitDuration = TimeSpan.FromMinutes(1);
		loop.Config.ConvertToMp4 = true;

		// 只看分段的用例文件名只有时间
		loop.Config.IncludeTitleInFileName = false;

		// 一次推进一分多钟，不能被当成空闲
		loop.Settings.StreamIdleTimeout = TimeSpan.FromHours(1);
		return loop;
	}

	private static byte[] Frame(VideoPacket packet, uint timestamp)
	{
		byte[] frame = FlvSamples.Video(VideoStyle.Avc, packet, timestamp);
		return FlvSamples.Tag(FlvSamples.VideoTag, timestamp, [.. frame.AsSpan(11, frame.Length - 15), .. new byte[FrameSize]]);
	}

	private static async Task WaitForFilesAsync(RecordingLoopHarness loop, int count)
	{
		using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
		string directory = RecordingFileLayout.GetRoomDirectory(loop.Config.RecordDirectory, RecordingLoopHarness.RoomId);

		while (Directory.GetFiles(directory, "*.flv").Length < count)
		{
			await Task.Delay(10, timeout.Token);
		}
	}

	private static async Task<RemuxInput> WaitForRemuxAsync(RecordingMediaTools mediaTools, int count)
	{
		using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));

		while (mediaTools.Remuxes.Count < count)
		{
			await Task.Delay(10, timeout.Token);
		}

		return mediaTools.Remuxes.ElementAt(count - 1);
	}

	// 第一次写入时通知
	private sealed class FirstWriteFile(string path, TaskCompletionSource written) : FileStream(path, new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.Read, Options = FileOptions.Asynchronous })
	{
		public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
		{
			written.TrySetResult();
			return base.WriteAsync(buffer, cancellationToken);
		}
	}

	// 不缓冲，写进来的数据立即落盘；关闭时一直等到 release 完成
	private sealed class StuckClosingFile(string path, Task release) : FileStream(path, new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.Read, Options = FileOptions.Asynchronous, BufferSize = 0 })
	{
		private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public Task Closed => _closed.Task;

		public override async ValueTask DisposeAsync()
		{
			await release;
			await base.DisposeAsync();
			_closed.TrySetResult();
		}
	}
}
