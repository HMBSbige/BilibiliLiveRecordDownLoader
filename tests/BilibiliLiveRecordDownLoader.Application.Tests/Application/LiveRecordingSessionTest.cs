using BilibiliLive.Models;
using BilibiliLiveRecordDownLoader.Application.Recording;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class LiveRecordingSessionTest
{
	private static LiveStreamCandidate Candidate(StreamFormat format, string signature, params string[] hosts)
	{
		return new LiveStreamCandidate(StreamProtocol.HttpHls, format, StreamCodec.Avc, 10000, [.. hosts.Select(host => new Uri($"https://{host}/live/index.m3u8?sign={signature}"))]);
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
		session.Candidates = [candidate];
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
		LiveRecordingSession session = new() { Candidates = [flv, ts] };

		session.MarkFailed(flv, flv.Urls[0]);
		bool afterOne = session.HasUntriedEndpoint;
		session.MarkFailed(ts, ts.Urls[0]);

		using (Assert.Multiple())
		{
			await Assert.That(afterOne).IsTrue();
			await Assert.That(session.HasUntriedEndpoint).IsFalse();
		}
	}

	[Test]
	public async Task StemIsReservedOnceAsync()
	{
		LiveRecordingSession session = new();

		using (Assert.Multiple())
		{
			await Assert.That(session.TryReserveStem(Path.Combine("records", "1", "20260102_030405"))).IsTrue();
			await Assert.That(session.TryReserveStem(Path.Combine("records", "1", "20260102_030405"))).IsFalse();
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
