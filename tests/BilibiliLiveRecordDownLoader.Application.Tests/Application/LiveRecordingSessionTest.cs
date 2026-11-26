using BilibiliLiveRecordDownLoader.Application.Recording;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class LiveRecordingSessionTest
{
	[Test]
	public async Task RestrictionIsReportedOnlyWhenItChangesAsync()
	{
		LiveRecordingSession session = new(NullLogger.Instance);
		bool unrestricted = session.TryUpdateRestriction(null);
		bool first = session.TryUpdateRestriction(new QualityRestriction.NotLoggedIn(StreamQuality.Original));
		bool same = session.TryUpdateRestriction(new QualityRestriction.NotLoggedIn(StreamQuality.Original));
		bool other = session.TryUpdateRestriction(new QualityRestriction.LoginMayHaveExpired(StreamQuality.Original));

		using (Assert.Multiple())
		{
			await Assert.That(unrestricted).IsFalse();
			await Assert.That(first).IsTrue();
			await Assert.That(same).IsFalse();
			await Assert.That(other).IsTrue();
		}
	}
}
