using BilibiliLive.Danmu;
using BilibiliLiveRecordDownLoader.Application.Contracts.Media;
using BilibiliLiveRecordDownLoader.Application.Recording;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using Microsoft.Extensions.Logging;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class RoomSplitRecordingTest
{
	private const int FrameSize = 40 << 10;

	private static byte[] Start => [.. FlvSamples.Header, .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.SequenceHeader, 0), .. Frame(VideoPacket.KeyFrame, 0)];

	[Test]
	[Arguments(true, null, 2)]
	[Arguments(true, false, 1)]
	[Arguments(false, true, 2)]
	[Arguments(false, null, 1)]
	public async Task TitleChangeWhileRecordingStartsANewFileAtTheNextKeyFrameAsync(bool global, bool? room, int expectedFiles, CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = RecordingLoopHarness.CreateSplitting(new RecordingMediaTools(), cancellationToken);
		loop.Config.SplitMode = RecordingSplitMode.None;
		StreamRequest stream = await StartRecordingAsync(loop);

		// 开始录制之后才打开的设置同样生效
		loop.Config.SplitOnTitleChange = global;
		loop.Settings = loop.Settings with { SplitOnTitleChange = room };
		loop.Session.Handle(new DanmuMessage.RoomChanged("new title", null, null));
		await stream.SendAndWaitAsync(Frame(VideoPacket.KeyFrame, 2000));
		await stream.SendAndWaitAsync(Frame(VideoPacket.InterFrame, 2033));
		await stream.SendAndWaitAsync(Frame(VideoPacket.KeyFrame, 4000));
		await stream.EndAsync();

		// 同一秒内的第二段文件名带序号
		string[] expected = expectedFiles is 2 ? ["20260101_000000.flv", "20260101_000000_1.flv"] : ["20260101_000000.flv"];
		await Assert.That(await RecordedFilesAfterTheAttemptAsync(loop)).IsEquivalentTo(expected, CollectionOrdering.Matching);
	}

	[Test]
	public async Task TitleChangeBeforeRecordingStartsDoesNotSplitAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = RecordingLoopHarness.CreateSplitting(new RecordingMediaTools(), cancellationToken);
		loop.Config.SplitMode = RecordingSplitMode.None;
		loop.Config.SplitOnTitleChange = true;

		loop.GoLive();
		StreamRequest stream = await loop.NextStreamAsync();
		loop.Session.Handle(new DanmuMessage.RoomChanged("new title", null, null));
		await stream.AcceptAsync(Start);
		await stream.SendAndWaitAsync(Frame(VideoPacket.InterFrame, 33));
		await stream.SendAndWaitAsync(Frame(VideoPacket.KeyFrame, 2000));
		await stream.EndAsync();

		await Assert.That(await RecordedFilesAfterTheAttemptAsync(loop)).IsEquivalentTo(["20260101_000000.flv"], CollectionOrdering.Matching);
	}

	[Test]
	public async Task FileStartedAfterATitleChangeIsNamedWithTheNewTitleAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = RecordingLoopHarness.CreateSplitting(new RecordingMediaTools(), cancellationToken);
		loop.Config.SplitMode = RecordingSplitMode.None;
		loop.Config.SplitOnTitleChange = true;
		loop.Config.IncludeTitleInFileName = true;
		StreamRequest stream = await StartRecordingAsync(loop);

		loop.Session.Handle(new DanmuMessage.RoomChanged("updated title", null, null));
		await stream.SendAndWaitAsync(Frame(VideoPacket.KeyFrame, 2000));
		await stream.SendAndWaitAsync(Frame(VideoPacket.KeyFrame, 4000));
		await stream.EndAsync();

		await Assert.That(await RecordedFilesAfterTheAttemptAsync(loop)).IsEquivalentTo(["20260101_000000_title.flv", "20260101_000000_updated title.flv"], CollectionOrdering.Matching);
	}

	[Test]
	public async Task TitleInFileNameFollowsTheSettingWhenEachFileStartsAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = RecordingLoopHarness.CreateSplitting(new RecordingMediaTools(), cancellationToken);
		loop.Config.IncludeTitleInFileName = true;
		StreamRequest stream = await StartRecordingAsync(loop);

		loop.Config.IncludeTitleInFileName = false;
		loop.Time.Advance(TimeSpan.FromSeconds(61));
		await stream.SendAndWaitAsync(Frame(VideoPacket.KeyFrame, 61_000));
		await stream.SendAndWaitAsync(Frame(VideoPacket.KeyFrame, 63_000));
		await stream.EndAsync();

		await Assert.That(await RecordedFilesAfterTheAttemptAsync(loop)).IsEquivalentTo(["20260101_000000_title.flv", "20260101_000101.flv"], CollectionOrdering.Matching);
	}

	[Test]
	public async Task EachFileIsPostProcessedOnceFinishedWhileTheRecordingTimeKeepsRunningAsync(CancellationToken cancellationToken)
	{
		RecordingMediaTools mediaTools = new();
		TaskCompletionSource nextFileWritten = new(TaskCreationOptions.RunContinuationsAsynchronously);
		int files = 0;
		await using RecordingLoopHarness loop = RecordingLoopHarness.CreateSplitting(mediaTools, cancellationToken, path => Interlocked.Increment(ref files) is 1 ? LiveRecorder.CreateRecordingFile(path) : new FirstWriteFile(path, nextFileWritten));
		StreamRequest stream = await StartRecordingAsync(loop);
		ActiveRecordingDto? first = loop.Snapshot.Recording;

		loop.Time.Advance(TimeSpan.FromSeconds(61));
		await stream.SendAndWaitAsync(Frame(VideoPacket.KeyFrame, 61_000));
		await stream.SendAndWaitAsync(Frame(VideoPacket.InterFrame, 61_033));
		await stream.SendAndWaitAsync(Frame(VideoPacket.KeyFrame, 63_000));
		RemuxInput finished = await WaitForRemuxAsync(loop, mediaTools, 1);

		// 写盘端换完文件才往新文件里写，这时新的一段已经开始
		await nextFileWritten.Task.WaitAsync(cancellationToken);
		ActiveRecordingDto? second = loop.Snapshot.Recording;

		await stream.EndAsync();
		RemuxInput last = await WaitForRemuxAsync(loop, mediaTools, 2);

		using (Assert.Multiple())
		{
			await Assert.That(first).IsNotNull();
			await Assert.That(second).IsEqualTo(first);
			await Assert.That(Directory.GetFiles(loop.RoomDirectory, "*.flv").Order(StringComparer.Ordinal)).IsEquivalentTo([finished.Source, last.Source], CollectionOrdering.Matching);
			await Assert.That(Path.GetFileNameWithoutExtension(last.Source)).IsEqualTo("20260101_000101");
		}
	}

	[Test]
	public async Task FileStillClosingWhenTheAttemptEndsIsPostProcessedOnceAfterClosingAndNothingFollowsAsync(CancellationToken cancellationToken)
	{
		RecordingMediaTools mediaTools = new();
		TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
		StuckClosingFile? first = null;
		await using RecordingLoopHarness loop = RecordingLoopHarness.CreateSplitting(mediaTools, cancellationToken, path => first is null ? first = new StuckClosingFile(path, release.Task) : LiveRecorder.CreateRecordingFile(path));
		StreamRequest stream = await StartRecordingAsync(loop);
		loop.Time.Advance(TimeSpan.FromSeconds(61));
		await stream.SendAndWaitAsync(Frame(VideoPacket.KeyFrame, 61_000));
		await stream.SendAndWaitAsync(Frame(VideoPacket.KeyFrame, 63_000));

		// 换文件时关旧文件卡住，下播后排空超时，录制循环把旧文件交给后处理；还没关掉时转封装会漏掉末尾
		loop.GoOffline();
		await stream.EndAsync();
		await loop.WaitUntilAsync(static snapshot => snapshot.RecordStatus is RecordStatus.Idle);
		await Task.Delay(200, cancellationToken);
		int remuxedWhileClosing = mediaTools.Remuxes.Count;
		DateTimeOffset? startedAt = loop.Snapshot.Recording?.StartedAt;
		release.SetResult();
		StuckClosingFile stuck = await Assert.That(first).IsNotNull();
		await stuck.Closed.WaitAsync(cancellationToken);
		await WaitForRemuxAsync(loop, mediaTools, 1);

		// 关闭之后有没有开始新的一段要在停下之前看，停下会结束录制循环；有没有多余的转封装要等后处理全部结束再看
		DateTimeOffset? startedAtAfterClosing = loop.Snapshot.Recording?.StartedAt;
		await loop.StopAsync();
		RemuxInput finished = await Assert.That(mediaTools.Remuxes).HasSingleItem();

		using (Assert.Multiple())
		{
			await Assert.That(remuxedWhileClosing).IsEqualTo(0);
			await Assert.That(Directory.GetFiles(loop.RoomDirectory, "*.flv")).IsEquivalentTo([finished.Source]);
			await Assert.That(startedAtAfterClosing).IsEqualTo(startedAt);
		}
	}

	// 磁盘一直不恢复时，退出也在等这次后处理，不能一直等下去
	[Test]
	public async Task FileThatStaysOpenIsLeftWithoutPostProcessingAsync(CancellationToken cancellationToken)
	{
		RecordingMediaTools mediaTools = new();
		TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
		StuckClosingFile? first = null;
		await using RecordingLoopHarness loop = RecordingLoopHarness.CreateSplitting(mediaTools, cancellationToken, path => first is null ? first = new StuckClosingFile(path, release.Task) : LiveRecorder.CreateRecordingFile(path));
		StreamRequest stream = await StartRecordingAsync(loop);
		loop.Time.Advance(TimeSpan.FromSeconds(61));
		await stream.SendAndWaitAsync(Frame(VideoPacket.KeyFrame, 61_000));
		await stream.SendAndWaitAsync(Frame(VideoPacket.KeyFrame, 63_000));
		loop.GoOffline();
		await stream.EndAsync();
		await loop.WaitUntilAsync(static snapshot => snapshot.RecordStatus is RecordStatus.Idle);
		StuckClosingFile stuck = await Assert.That(first).IsNotNull();
		string skipped = $"{stuck.Name} is still being written, leaving it without post-processing";

		// 后处理开始等文件关闭时才计时，按步推进
		for (int step = 0; step < 10 && !loop.Logs.Any(log => log.Message == skipped); ++step)
		{
			loop.Time.Advance(TimeSpan.FromMinutes(1));
			await Task.Delay(50, cancellationToken);
		}

		release.SetResult();
		await stuck.Closed.WaitAsync(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(loop.Logs).Contains(log => log is { Level: LogLevel.Warning } && log.Message == skipped);
			await Assert.That(mediaTools.Remuxes).IsEmpty();
		}
	}

	[Test]
	public async Task SplittingTurnedOnWhileRecordingSplitsTheCurrentRecordingAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = RecordingLoopHarness.CreateSplitting(new RecordingMediaTools(), cancellationToken);
		loop.Config.SplitMode = RecordingSplitMode.None;
		StreamRequest stream = await StartRecordingAsync(loop);

		loop.Config.SplitMode = RecordingSplitMode.Duration;
		loop.Time.Advance(TimeSpan.FromSeconds(61));
		await stream.SendAndWaitAsync(Frame(VideoPacket.KeyFrame, 61_000));
		await stream.SendAndWaitAsync(Frame(VideoPacket.InterFrame, 61_033));
		await stream.SendAndWaitAsync(Frame(VideoPacket.KeyFrame, 63_000));
		await stream.EndAsync();

		await Assert.That(await RecordedFilesAfterTheAttemptAsync(loop)).IsEquivalentTo(["20260101_000000.flv", "20260101_000101.flv"], CollectionOrdering.Matching);
	}

	[Test]
	public async Task ConversionFollowsTheSettingWhenEachFileIsFinishedAsync(CancellationToken cancellationToken)
	{
		RecordingMediaTools mediaTools = new();
		await using RecordingLoopHarness loop = RecordingLoopHarness.CreateSplitting(mediaTools, cancellationToken);
		loop.Config.ConvertToMp4 = false;
		StreamRequest stream = await StartRecordingAsync(loop);
		loop.Time.Advance(TimeSpan.FromSeconds(61));
		await stream.SendAndWaitAsync(Frame(VideoPacket.KeyFrame, 61_000));
		await stream.SendAndWaitAsync(Frame(VideoPacket.KeyFrame, 63_000));

		// 新文件建好时旧文件已经按当时的设置交给了后处理
		await loop.WaitUntilAsync(() => Directory.GetFiles(loop.RoomDirectory, "*.flv").Length >= 2, "2 recordings exist");
		loop.Config.ConvertToMp4 = true;
		await stream.EndAsync();
		await loop.StopAsync();
		RemuxInput converted = await Assert.That(mediaTools.Remuxes).HasSingleItem();

		await Assert.That(Path.GetFileNameWithoutExtension(converted.Source)).IsEqualTo("20260101_000101");
	}

	private static async Task<StreamRequest> StartRecordingAsync(RecordingLoopHarness loop)
	{
		loop.GoLive();
		StreamRequest stream = await loop.NextStreamAsync();
		await stream.AcceptAsync(Start);
		await stream.SendAndWaitAsync(Frame(VideoPacket.InterFrame, 33));
		return stream;
	}

	// 重连时上一次尝试已经结束；文件都超过后处理的下限，不会被删掉
	private static async Task<string[]> RecordedFilesAfterTheAttemptAsync(RecordingLoopHarness loop)
	{
		_ = await loop.NextStreamAsync();
		return [.. Directory.GetFiles(loop.RoomDirectory, "*.flv").Select(static path => Path.GetFileName(path)).Order(StringComparer.Ordinal)];
	}

	private static byte[] Frame(VideoPacket packet, uint timestamp)
	{
		return FlvSamples.PaddedVideo(packet, timestamp, FrameSize);
	}

	private static async Task<RemuxInput> WaitForRemuxAsync(RecordingLoopHarness loop, RecordingMediaTools mediaTools, int count)
	{
		await loop.WaitUntilAsync(() => mediaTools.Remuxes.Count >= count, $"{count} remuxes are queued");
		return mediaTools.Remuxes.ElementAt(count - 1);
	}

	private sealed class FirstWriteFile(string path, TaskCompletionSource written) : FileStream(path, new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.Read, Options = FileOptions.Asynchronous })
	{
		public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
		{
			written.TrySetResult();
			return base.WriteAsync(buffer, cancellationToken);
		}
	}

	// 不缓冲，关闭卡住时数据也已经落盘
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
