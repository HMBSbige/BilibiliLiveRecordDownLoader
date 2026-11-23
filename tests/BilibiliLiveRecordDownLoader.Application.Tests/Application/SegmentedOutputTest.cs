using BilibiliLiveRecordDownLoader.Application.Recording;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using System.Collections.Concurrent;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class SegmentedOutputTest
{
	private static readonly RecordingSplit ThreeUnits = new RecordingSplit.BySize(100);

	// 交出时记下已经建了哪些文件和它们关闭时的内容：没关闭的文件内容为空，下一段建得太早会多出一个
	[Test]
	public async Task FinishedSegmentIsClosedAndHandedOverBeforeTheNextOneOpensAsync(CancellationToken cancellationToken)
	{
		ConcurrentQueue<string> handOvers = new();
		SplitRecording recording = new();
		recording.SegmentCompleted = path => handOvers.Enqueue($"{path}: {FakeSplitter.Describe(recording.Files)}");

		await recording.RunAsync(new FakeSplitter(), ThreeUnits, static session => WriteUnitsAsync(session, 7), cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(handOvers).IsEquivalentTo(["0: 012", "1: 012 | H0:345"], CollectionOrdering.Matching);
			await Assert.That(FakeSplitter.Describe(recording.Files)).IsEqualTo("012 | H0:345 | H0:6");
		}
	}

	[Test]
	public async Task FailingToCreateTheNextFileIsAWriteFailureAsync(CancellationToken cancellationToken)
	{
		SplitRecording recording = new() { FailOpeningFile = 1 };

		await Assert.ThrowsAsync<RecordingWriteException>(() => recording.RunAsync(new FakeSplitter(), ThreeUnits, static session => WriteUnitsAsync(session, int.MaxValue), cancellationToken)).WithInnerException().WithMessage("cannot create");

		using (Assert.Multiple())
		{
			await Assert.That(recording.Completed).IsEquivalentTo(["0"], CollectionOrdering.Matching);
			await Assert.That(FakeSplitter.Describe(recording.Files)).IsEqualTo("012");
		}
	}

	[Test]
	public async Task FailingToCloseAFinishedSegmentIsAWriteFailureAsync(CancellationToken cancellationToken)
	{
		SplitRecording recording = new() { FailClosingFile = 0 };

		await Assert.ThrowsAsync<RecordingWriteException>(() => recording.RunAsync(new FakeSplitter(), ThreeUnits, static session => WriteUnitsAsync(session, int.MaxValue), cancellationToken)).WithInnerException().WithMessage("cannot flush");

		// 没有交出去的一段由录制循环最后处理
		using (Assert.Multiple())
		{
			await Assert.That(recording.Completed).IsEmpty();
			await Assert.That(recording.Files).Count().IsEqualTo(1);
		}
	}

	[Test]
	public async Task SegmentStillClosingWhenTheDrainTimesOutIsNeitherHandedOverNorFollowedByANewFileAsync(CancellationToken cancellationToken)
	{
		TaskCompletionSource closing = new(TaskCreationOptions.RunContinuationsAsynchronously);
		SplitRecording recording = new() { BlockClosingFile = (0, closing.Task), DrainTimeout = TimeSpan.FromMilliseconds(200) };

		await Assert.ThrowsExactlyAsync<RecordingWriteException>(() => recording.RunAsync(new FakeSplitter(), ThreeUnits, static session => WriteUnitsAsync(session, 7), cancellationToken));
		closing.SetResult();
		await recording.WaitForClosingAsync(cancellationToken);
		await Task.Delay(200, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(recording.Completed).IsEmpty();
			await Assert.That(recording.Files).Count().IsEqualTo(1);
		}
	}

	private static async Task WriteUnitsAsync(SessionWriter session, int count)
	{
		for (int unit = 0; unit < count; ++unit)
		{
			session.CancellationToken.ThrowIfCancellationRequested();
			await session.WriteAsync(FakeSplitter.Unit(((char)('0' + unit % 10)).ToString(), 30));
		}
	}
}
