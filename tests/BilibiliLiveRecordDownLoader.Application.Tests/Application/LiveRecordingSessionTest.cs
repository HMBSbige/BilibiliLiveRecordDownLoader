using BilibiliLiveRecordDownLoader.Application.Recording;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class LiveRecordingSessionTest
{
	[Test]
	public async Task RestrictionIsReportedOnlyWhenItChangesAsync()
	{
		LiveRecordingSession session = new(NullLogger.Instance);
		bool unrestricted = session.TryUpdateRestriction(null);
		bool first = session.TryUpdateRestriction(new QualityRestriction.NotLoggedIn(10000));
		bool same = session.TryUpdateRestriction(new QualityRestriction.NotLoggedIn(10000));
		bool other = session.TryUpdateRestriction(new QualityRestriction.LoginMayHaveExpired(10000));

		using (Assert.Multiple())
		{
			await Assert.That(unrestricted).IsFalse();
			await Assert.That(first).IsTrue();
			await Assert.That(same).IsFalse();
			await Assert.That(other).IsTrue();
		}
	}
}
