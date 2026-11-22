using BilibiliLiveRecordDownLoader.Application.Recording;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class SegmentedOutputTest
{
	private static readonly RecordingSplit ThreeUnits = new RecordingSplit.BySize(100);

	[Test]
	[Timeout(10_000)]
	public async Task FinishedSegmentIsClosedAndHandedOverBeforeTheNextOneOpensAsync(CancellationToken cancellationToken)
	{
		SplitRecording recording = new();

		await recording.RunAsync(new FakeSplitter(), ThreeUnits, session => WriteUnitsAsync(session, 7), cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(RecordingSegmenterTest.Describe(recording.Files)).IsEqualTo("012 | H0:345 | H0:6");
			await Assert.That(string.Join(", ", recording.Completed)).IsEqualTo("0, 1");
		}
	}

	[Test]
	[Timeout(10_000)]
	public async Task FailingToCreateTheNextFileIsAWriteFailureAsync(CancellationToken cancellationToken)
	{
		SplitRecording recording = new() { FailOpeningFile = 1 };

		RecordingWriteException? exception = await Assert.ThrowsExactlyAsync<RecordingWriteException>(() => recording.RunAsync(new FakeSplitter(), ThreeUnits, session => WriteUnitsAsync(session, int.MaxValue), cancellationToken));

		using (Assert.Multiple())
		{
			await Assert.That(exception?.InnerException?.Message).IsEqualTo("cannot create");
			await Assert.That(string.Join(", ", recording.Completed)).IsEqualTo("0");
			await Assert.That(RecordingSegmenterTest.Describe(recording.Files)).IsEqualTo("012");
		}
	}

	[Test]
	[Timeout(10_000)]
	public async Task FailingToCloseAFinishedSegmentIsAWriteFailureAsync(CancellationToken cancellationToken)
	{
		SplitRecording recording = new() { FailClosingFile = 0 };

		RecordingWriteException? exception = await Assert.ThrowsExactlyAsync<RecordingWriteException>(() => recording.RunAsync(new FakeSplitter(), ThreeUnits, session => WriteUnitsAsync(session, int.MaxValue), cancellationToken));

		// 没有交出去的一段由录制循环最后处理
		using (Assert.Multiple())
		{
			await Assert.That(exception?.InnerException?.Message).IsEqualTo("cannot flush");
			await Assert.That(recording.Completed).IsEmpty();
			await Assert.That(recording.Files.Count).IsEqualTo(1);
		}
	}

	[Test]
	[Timeout(10_000)]
	public async Task SegmentStillClosingWhenTheDrainTimesOutIsNeitherHandedOverNorFollowedByANewFileAsync(CancellationToken cancellationToken)
	{
		TaskCompletionSource closing = new(TaskCreationOptions.RunContinuationsAsynchronously);
		SplitRecording recording = new() { BlockClosingFile = (0, closing.Task), DrainTimeout = TimeSpan.FromMilliseconds(200) };

		await Assert.ThrowsExactlyAsync<RecordingWriteException>(() => recording.RunAsync(new FakeSplitter(), ThreeUnits, session => WriteUnitsAsync(session, 7), cancellationToken));
		closing.SetResult();
		await recording.WaitForClosingAsync(cancellationToken);
		await Task.Delay(200, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(recording.Completed).IsEmpty();
			await Assert.That(recording.Files.Count).IsEqualTo(1);
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
