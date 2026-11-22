using BilibiliLive.Models;
using BilibiliLiveRecordDownLoader.Application.Recording;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class LiveRecordingSessionTest
{
	private static LiveStreamCandidate Candidate(StreamFormat format, string signature, params string[] hosts)
	{
		return new LiveStreamCandidate.Listed(StreamProtocol.HttpHls, format, StreamCodec.Avc, 10000, [], [.. hosts.Select(host => new Uri($"https://{host}/live/index.m3u8?sign={signature}"))]);
	}

	private static string Describe(IEnumerable<LiveStreamCandidate> candidates)
	{
		return string.Join(", ", candidates.SelectMany(candidate => candidate.Urls, (candidate, uri) => $"{candidate.Format}@{uri.Host}"));
	}

	[Test]
	public async Task FailedEndpointMovesToTheNextHostThenTheNextCandidateAsync()
	{
		LiveRecordingSession session = new();
		LiveStreamCandidate fmp4 = Candidate(StreamFormat.Fmp4, "a", "a.test", "b.test");
		LiveStreamCandidate ts = Candidate(StreamFormat.Ts, "a", "a.test");
		LiveStreamCandidate[] candidates = [fmp4, ts];

		session.MarkFailed(fmp4, fmp4.Urls[0]);
		string afterFirst = Describe(session.GetUntriedCandidates(candidates));
		session.MarkFailed(fmp4, fmp4.Urls[1]);
		string afterSecond = Describe(session.GetUntriedCandidates(candidates));

		using (Assert.Multiple())
		{
			await Assert.That(afterFirst).IsEqualTo("Fmp4@b.test, Ts@a.test");
			await Assert.That(afterSecond).IsEqualTo("Ts@a.test");
		}
	}

	[Test]
	public async Task FailureIsRememberedAcrossReSignedUrlsAsync()
	{
		LiveRecordingSession session = new();
		session.MarkFailed(Candidate(StreamFormat.Fmp4, "old", "a.test"), new Uri("https://a.test/live/index.m3u8?sign=old"));

		IReadOnlyList<LiveStreamCandidate> untried = session.GetUntriedCandidates([Candidate(StreamFormat.Fmp4, "new", "a.test", "b.test")]);

		await Assert.That(Describe(untried)).IsEqualTo("Fmp4@b.test");
	}

	[Test]
	public async Task ResetForgetsFailuresAndCandidatesAsync()
	{
		LiveRecordingSession session = new();
		LiveStreamCandidate candidate = Candidate(StreamFormat.Flv, "a", "a.test");
		session.Candidates = new CandidateBatch([candidate], false);
		session.MarkFailed(candidate, candidate.Urls[0]);
		bool exhausted = session.GetUntriedCandidates([candidate]).Count is 0;

		session.Reset();

		using (Assert.Multiple())
		{
			await Assert.That(exhausted).IsTrue();
			await Assert.That(session.Candidates).IsNull();
			await Assert.That(session.GetUntriedCandidates([candidate]).Count).IsEqualTo(1);
		}
	}

	[Test]
	public async Task SessionIsExhaustedOnceEveryEndpointOfEveryCandidateFailedAsync()
	{
		LiveStreamCandidate flv = Candidate(StreamFormat.Flv, "a", "a.test");
		LiveStreamCandidate ts = Candidate(StreamFormat.Ts, "a", "a.test");
		LiveRecordingSession session = new() { Candidates = new CandidateBatch([flv, ts], false) };

		session.MarkFailed(flv, flv.Urls[0]);
		bool afterOne = session.HasUntriedEndpoint;
		session.MarkFailed(ts, ts.Urls[0]);

		using (Assert.Multiple())
		{
			await Assert.That(afterOne).IsTrue();
			await Assert.That(session.HasUntriedEndpoint).IsFalse();
		}
	}

	// 主播放列表的变体和接口给的 fMP4 在同一个节点上
	[Test]
	public async Task FailedVariantLeavesOtherStreamsOfTheSameHostUntriedAsync()
	{
		LiveStreamCandidate.Variant trueOriginal = new(StreamCodec.Avc, 25000, [new Uri("https://a.test/live-bvc/1/live_a_b_c/index.m3u8?sign=a")]);
		LiveStreamCandidate.Variant original = new(StreamCodec.Avc, 10000, [new Uri("https://a.test/live-bvc/1/live_a_b_c_bluray/index.m3u8?sign=a")]);
		LiveStreamCandidate fmp4 = Candidate(StreamFormat.Fmp4, "a", "a.test");
		LiveStreamCandidate[] candidates = [trueOriginal, original, fmp4];
		LiveRecordingSession session = new() { Candidates = new CandidateBatch(candidates, false) };

		session.MarkFailed(trueOriginal, trueOriginal.Urls[0]);
		string afterTrueOriginal = Describe(session.GetUntriedCandidates(candidates));
		session.MarkFailed(original, original.Urls[0]);

		using (Assert.Multiple())
		{
			await Assert.That(afterTrueOriginal).IsEqualTo("Fmp4@a.test, Fmp4@a.test");
			await Assert.That(session.GetUntriedCandidates(candidates).Single() is LiveStreamCandidate.Listed { Format: StreamFormat.Fmp4 }).IsTrue();
			await Assert.That(session.HasUntriedEndpoint).IsTrue();
		}
	}

	[Test]
	public async Task RestrictionIsReportedOnlyWhenItChangesAsync()
	{
		LiveRecordingSession session = new();
		bool unrestricted = session.TryUpdateRestriction(null);
		bool first = session.TryUpdateRestriction(new QualityRestriction.NotLoggedIn(10000));
		bool same = session.TryUpdateRestriction(new QualityRestriction.NotLoggedIn(10000));
		// 换线路和重新请求候选流时会重置，提醒状态要留到下播
		session.Reset();
		bool afterReset = session.TryUpdateRestriction(new QualityRestriction.NotLoggedIn(10000));
		bool other = session.TryUpdateRestriction(new QualityRestriction.LoginMayHaveExpired(10000));

		using (Assert.Multiple())
		{
			await Assert.That(unrestricted).IsFalse();
			await Assert.That(first).IsTrue();
			await Assert.That(same).IsFalse();
			await Assert.That(afterReset).IsFalse();
			await Assert.That(other).IsTrue();
		}
	}

	[Test]
	[Arguments(0, true)]
	[Arguments(29, true)]
	[Arguments(30, false)]
	[Arguments(3600, false)]
	public async Task AttemptWithLittleDataIsAQuickFailureAsync(int activeSeconds, bool expected)
	{
		RecordingAttempt attempt = new(Candidate(StreamFormat.Flv, "a", "a.test"), new Uri("https://a.test/"), TimeSpan.FromSeconds(activeSeconds), new RecordingEnd.Completed(StreamEndReason.EndOfStream));

		await Assert.That(attempt.IsQuickFailure).IsEqualTo(expected);
	}

	[Test]
	public async Task NewTimelineRightAfterStartIsNotAQuickFailureAsync()
	{
		RecordingAttempt attempt = new(Candidate(StreamFormat.Ts, "a", "a.test"), new Uri("https://a.test/"), TimeSpan.FromSeconds(2), new RecordingEnd.Completed(StreamEndReason.Discontinuity));

		await Assert.That(attempt.IsQuickFailure).IsFalse();
	}
}
