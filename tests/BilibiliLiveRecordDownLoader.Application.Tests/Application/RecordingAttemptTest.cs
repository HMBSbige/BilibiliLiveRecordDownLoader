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
		RecordingAttempt attempt = new(new LiveStreamCandidate.Listed(StreamProtocol.HttpHls, StreamFormat.Ts, StreamCodec.Avc, 10000, [], [new Uri("https://a.test/")]), new Uri("https://a.test/"), null, TimeSpan.FromSeconds(activeSeconds), new RecordingEnd.Completed(reason));

		using (Assert.Multiple())
		{
			await Assert.That(attempt.IsQuickFailure).IsEqualTo(isQuickFailure);
			await Assert.That(attempt.IsSustained).IsEqualTo(isSustained);
		}
	}
}
