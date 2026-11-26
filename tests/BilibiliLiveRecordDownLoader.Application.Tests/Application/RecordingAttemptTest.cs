using BilibiliLive.Models;
using BilibiliLiveRecordDownLoader.Application.Recording;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class RecordingAttemptTest
{
	[Test]
	[Arguments(29, StreamEndReason.EndOfStream, true, false)]
	[Arguments(30, StreamEndReason.EndOfStream, false, true)]
	[Arguments(2, StreamEndReason.Discontinuity, false, false)]
	public async Task AttemptIsSustainedOrAQuickFailureByActiveDurationAsync(int activeSeconds, StreamEndReason reason, bool isQuickFailure, bool isSustained)
	{
		RecordingAttempt attempt = CreateAttempt(TimeSpan.FromSeconds(activeSeconds), new RecordingEnd.Completed(reason));

		using (Assert.Multiple())
		{
			await Assert.That(attempt.IsQuickFailure).IsEqualTo(isQuickFailure);
			await Assert.That(attempt.IsSustained).IsEqualTo(isSustained);
		}
	}

	[Test]
	[Arguments(true, 29, false)]
	[Arguments(true, 30, true)]
	[Arguments(false, 0, true)]
	public async Task DiskHasWorkedUnlessItFailedBeforeWritingLongEnoughAsync(bool isWriteFailed, int writtenSeconds, bool hasDiskWorked)
	{
		RecordingEnd end = isWriteFailed ? new RecordingEnd.WriteFailed(new RecordingWriteException(new IOException("disk full")), TimeSpan.FromSeconds(writtenSeconds)) : new RecordingEnd.Completed(StreamEndReason.EndOfStream);
		RecordingAttempt attempt = CreateAttempt(TimeSpan.FromMinutes(1), end);

		await Assert.That(attempt.HasDiskWorked).IsEqualTo(hasDiskWorked);
	}

	private static RecordingAttempt CreateAttempt(TimeSpan activeDuration, RecordingEnd end)
	{
		Uri endpoint = new("https://a.test/");
		return new RecordingAttempt(new LiveStreamCandidate.Listed(StreamProtocol.HttpHls, StreamFormat.Ts, StreamCodec.Avc, 10000, [], [endpoint]), endpoint, null, activeDuration, end);
	}
}
