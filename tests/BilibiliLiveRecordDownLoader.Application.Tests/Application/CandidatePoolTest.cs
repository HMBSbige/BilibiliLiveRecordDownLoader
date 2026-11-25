using BilibiliLive.Models;
using BilibiliLiveRecordDownLoader.Application.Recording;
using System.Globalization;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class CandidatePoolTest
{
	private static readonly TimeSpan Long = RecordingAttempt.QuickFailureThreshold + TimeSpan.FromSeconds(1);

	private static readonly TimeSpan Short = TimeSpan.FromSeconds(1);

	private static readonly RecordingEnd DiskFull = new RecordingEnd.WriteFailed(new RecordingWriteException(new IOException("disk full")));

	private enum Open
	{
		Opens,
		Fails,
		Rejected
	}

	private static LiveStreamCandidate.Listed Candidate(StreamFormat format, string signature, params string[] hosts)
	{
		return new LiveStreamCandidate.Listed(StreamProtocol.HttpHls, format, StreamCodec.Avc, 10000, [], [.. hosts.Select(host => new Uri($"https://{host}/live/index.m3u8?sign={signature}"))]);
	}

	private static LiveStreamCandidate[] TwoHosts(string signature)
	{
		return [Candidate(StreamFormat.Fmp4, signature, "a.test", "b.test")];
	}

	private static LiveStreamCandidate[] TwoStreams(string signature)
	{
		return [Candidate(StreamFormat.Fmp4, signature, "a.test", "b.test"), Candidate(StreamFormat.Ts, signature, "a.test")];
	}

	private static Open Always(LiveStreamCandidate candidate, Uri uri)
	{
		return Open.Opens;
	}

	private static Open Never(LiveStreamCandidate candidate, Uri uri)
	{
		return Open.Fails;
	}

	private static Open OnlyB(LiveStreamCandidate candidate, Uri uri)
	{
		return uri.Host is "b.test" ? Open.Opens : Open.Fails;
	}

	private static Open RejectFmp4(LiveStreamCandidate candidate, Uri uri)
	{
		return candidate.Format is StreamFormat.Fmp4 ? Open.Rejected : Open.Opens;
	}

	[Test]
	public async Task OpenFailureMovesToTheNextHostThenTheNextCandidateAsync()
	{
		Pool pool = new(TwoStreams);

		string first = await pool.OpenAsync(static (candidate, _) => candidate.Format is StreamFormat.Ts ? Open.Opens : Open.Fails);
		string second = await pool.OpenAsync(Always);

		using (Assert.Multiple())
		{
			await Assert.That(first).IsEqualTo("Fmp4@a.test?sign=1, Fmp4@b.test?sign=1, Ts@a.test?sign=1");
			await Assert.That(second).IsEqualTo("Ts@a.test?sign=1");
			await Assert.That(pool.Requests).IsEqualTo(1);
		}
	}

	[Test]
	public async Task FailedVariantLeavesOtherStreamsOfTheSameHostUntriedAsync()
	{
		Pool pool = new(static sign =>
		[
			new LiveStreamCandidate.Variant(StreamCodec.Avc, 25000, null, [new Uri($"https://a.test/live-bvc/1/live_a_b_c/index.m3u8?sign={sign}")]),
			new LiveStreamCandidate.Variant(StreamCodec.Avc, 10000, null, [new Uri($"https://a.test/live-bvc/1/live_a_b_c_bluray/index.m3u8?sign={sign}")]),
			Candidate(StreamFormat.Fmp4, sign, "a.test")
		]);

		string first = await pool.OpenAsync(static (candidate, _) => candidate is LiveStreamCandidate.Variant { Qn: 10000 } ? Open.Opens : Open.Fails);
		string second = await pool.OpenAsync(static (candidate, _) => candidate is LiveStreamCandidate.Listed ? Open.Opens : Open.Fails);

		using (Assert.Multiple())
		{
			await Assert.That(first).IsEqualTo("Variant25000@a.test?sign=1, Variant10000@a.test?sign=1");
			await Assert.That(second).IsEqualTo("Variant10000@a.test?sign=1, Fmp4@a.test?sign=1");
		}
	}

	// RecordingEnd 和 QualityRestriction 是 internal 类型，不能做 public 测试方法的参数
	[Test]
	[Arguments(true, false, false, "Fmp4@a.test?sign=1", 1, DisplayName = "Long recording keeps the batch and starts over from the preferred host")]
	[Arguments(true, true, false, "Fmp4@a.test?sign=1", 1, DisplayName = "Long recording ending in a write failure starts over from the preferred host")]
	[Arguments(true, false, true, "Fmp4@a.test?sign=2", 2, DisplayName = "Restricted long recording requests again")]
	[Arguments(false, true, false, "Fmp4@b.test?sign=1", 1, DisplayName = "Quick write failure retries the same host")]
	public async Task AttemptEndDecidesWhichUrlIsOpenedNextAsync(bool isSustained, bool writeFailed, bool isRestricted, string expected, int requests)
	{
		Pool pool = new(TwoHosts);

		await pool.OpenAsync(OnlyB);
		pool.End(isSustained ? Long : Short, writeFailed ? DiskFull : null, isRestricted ? new QualityRestriction.NotLoggedIn(25000) : null);
		string next = await pool.OpenAsync(Always);

		using (Assert.Multiple())
		{
			await Assert.That(next).IsEqualTo(expected);
			await Assert.That(pool.Requests).IsEqualTo(requests);
		}
	}

	[Test]
	public async Task VerifiedBatchIsReplacedOnlyOnceWhenTheNewOneFailsTooAsync()
	{
		Pool pool = new(TwoHosts);

		await pool.OpenAsync(Always);
		pool.End(Long);
		string failed = await pool.OpenAsync(Never);
		int requestsAfterFailure = pool.Requests;
		string next = await pool.OpenAsync(Always);

		using (Assert.Multiple())
		{
			await Assert.That(failed).IsEqualTo("Fmp4@a.test?sign=1, Fmp4@b.test?sign=1, Fmp4@a.test?sign=2, Fmp4@b.test?sign=2, none");
			await Assert.That(requestsAfterFailure).IsEqualTo(2);
			await Assert.That(next).IsEqualTo("Fmp4@a.test?sign=3");
		}
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task QuickFailureMarksTheHostAndWaitsOnceEveryHostFailedAsync(bool isRestricted)
	{
		QualityRestriction? restriction = isRestricted ? new QualityRestriction.NotLoggedIn(25000) : null;
		Pool pool = new(TwoHosts);

		string first = await pool.OpenAsync(Always);
		AttemptVerdict afterFirst = pool.End(Short, restriction: restriction);
		string second = await pool.OpenAsync(Always);
		AttemptVerdict afterSecond = pool.End(Short, restriction: restriction);
		int requestsAfterFailures = pool.Requests;
		string next = await pool.OpenAsync(Always);

		using (Assert.Multiple())
		{
			await Assert.That((first, second)).IsEqualTo(("Fmp4@a.test?sign=1", "Fmp4@b.test?sign=1"));
			await Assert.That((afterFirst, afterSecond)).IsEqualTo((AttemptVerdict.Reconnect, AttemptVerdict.WaitBeforeReconnect));
			await Assert.That(requestsAfterFailures).IsEqualTo(1);
			await Assert.That(next).IsEqualTo("Fmp4@a.test?sign=2");
		}
	}

	[Test]
	public async Task VerifiedBatchFailingQuicklyIsReplacedWithoutWaitingOnlyOnceAsync()
	{
		Pool pool = new(TwoHosts);

		await pool.OpenAsync(Always);
		pool.End(Long);
		List<(string Opened, AttemptVerdict Verdict)> rounds = [];

		for (int i = 0; i < 4; ++i)
		{
			string opened = await pool.OpenAsync(Always);
			rounds.Add((opened, pool.End(Short)));
		}

		using (Assert.Multiple())
		{
			await Assert.That(rounds).IsEquivalentTo([("Fmp4@a.test?sign=1", AttemptVerdict.Reconnect), ("Fmp4@b.test?sign=1", AttemptVerdict.Reconnect), ("Fmp4@a.test?sign=2", AttemptVerdict.Reconnect), ("Fmp4@b.test?sign=2", AttemptVerdict.WaitBeforeReconnect)], CollectionOrdering.Matching);
			await Assert.That(pool.Requests).IsEqualTo(2);
		}
	}

	[Test]
	public async Task RejectedStreamOfAVerifiedBatchIsSignedAgainAsync()
	{
		Pool pool = new(TwoStreams);

		await pool.OpenAsync(Always);
		pool.End(Long);
		string next = await pool.OpenAsync(static (candidate, uri) => candidate.Format is StreamFormat.Fmp4 && uri.Query is "?sign=1" ? Open.Rejected : Open.Opens);

		using (Assert.Multiple())
		{
			await Assert.That(next).IsEqualTo("Fmp4@a.test?sign=1, Fmp4@b.test?sign=1, Fmp4@a.test?sign=2");
			await Assert.That(pool.Requests).IsEqualTo(2);
		}
	}

	[Test]
	public async Task RejectedStreamOfAnUnverifiedBatchFallsBackWithoutRequestingAgainAsync()
	{
		Pool pool = new(TwoStreams);

		string opened = await pool.OpenAsync(RejectFmp4);

		using (Assert.Multiple())
		{
			await Assert.That(opened).IsEqualTo("Fmp4@a.test?sign=1, Fmp4@b.test?sign=1, Ts@a.test?sign=1");
			await Assert.That(pool.Requests).IsEqualTo(1);
		}
	}

	[Test]
	public async Task StreamRejectedBeforeTheBatchWasVerifiedDoesNotRetireItAsync()
	{
		Pool pool = new(static sign => [Candidate(StreamFormat.Fmp4, sign, "a.test"), Candidate(StreamFormat.Ts, sign, "a.test")]);

		await pool.OpenAsync(RejectFmp4);
		pool.End(Long);
		string next = await pool.OpenAsync(RejectFmp4);

		using (Assert.Multiple())
		{
			await Assert.That(next).IsEqualTo("Fmp4@a.test?sign=1, Ts@a.test?sign=1");
			await Assert.That(pool.Requests).IsEqualTo(1);
		}
	}

	[Test]
	public async Task ShortRecordingOfANewTimelineStartsOverWithoutVerifyingTheBatchAsync()
	{
		Pool pool = new(TwoHosts);

		await pool.OpenAsync(OnlyB);
		pool.End(Short, new RecordingEnd.Completed(StreamEndReason.Discontinuity));
		string restarted = await pool.OpenAsync(Always);
		pool.End(Short);
		await pool.OpenAsync(Always);
		AttemptVerdict verdict = pool.End(Short);

		using (Assert.Multiple())
		{
			await Assert.That(restarted).IsEqualTo("Fmp4@a.test?sign=1");
			await Assert.That(verdict).IsEqualTo(AttemptVerdict.WaitBeforeReconnect);
			await Assert.That(pool.Requests).IsEqualTo(1);
		}
	}

	[Test]
	public async Task FailureIsRememberedAcrossReSignedUrlsAsync()
	{
		Pool pool = new(TwoHosts);

		await pool.OpenAsync(OnlyB);
		pool.Request = pool.Request with { HasCookie = true };
		string next = await pool.OpenAsync(Always);

		await Assert.That(next).IsEqualTo("Fmp4@b.test?sign=2");
	}

	[Test]
	public async Task ChangedRequestRetriesFailedHostsWhenTheNewBatchHasNoOtherAsync()
	{
		Pool pool = new(static sign => sign is "1" ? TwoHosts(sign) : [Candidate(StreamFormat.Fmp4, sign, "a.test")]);

		await pool.OpenAsync(OnlyB);
		pool.Request = pool.Request with { HasCookie = true };
		string next = await pool.OpenAsync(Always);

		using (Assert.Multiple())
		{
			await Assert.That(next).IsEqualTo("Fmp4@a.test?sign=2");
			await Assert.That(pool.Requests).IsEqualTo(2);
		}
	}

	// 和录制时一样逐个打开候选流的主机，打开失败就标记
	private sealed class Pool(Func<string, LiveStreamCandidate[]> create)
	{
		private readonly CandidatePool _pool = new(NullLogger.Instance);
		private (LiveStreamCandidate Candidate, Uri Endpoint)? _opened;

		public CandidateRequest Request { get; set; } = new(false, default, [], []);

		public int Requests { get; private set; }

		public async Task<string> OpenAsync(Func<LiveStreamCandidate, Uri, Open> opens)
		{
			List<string> tried = [];
			_opened = null;

			await foreach ((_, LiveStreamCandidate candidate) in _pool.GetCandidatesAsync(Request, FetchAsync, CancellationToken.None))
			{
				string label = candidate switch
				{
					LiveStreamCandidate.Listed listed => listed.Format.ToString(),
					LiveStreamCandidate.Variant variant => $"Variant{variant.Qn}"
				};

				foreach (Uri uri in candidate.Urls)
				{
					tried.Add($"{label}@{uri.Host}{uri.Query}");
					Open result = opens(candidate, uri);

					if (result is Open.Opens)
					{
						_opened = (candidate, uri);
						return string.Join(", ", tried);
					}

					_pool.MarkFailed(candidate, uri, result is Open.Rejected);
				}
			}

			tried.Add("none");
			return string.Join(", ", tried);
		}

		public AttemptVerdict End(TimeSpan activeDuration, RecordingEnd? end = null, QualityRestriction? restriction = null)
		{
			(LiveStreamCandidate candidate, Uri endpoint) = _opened ?? throw new InvalidOperationException("No stream is open.");
			return _pool.OnAttemptEnded(new RecordingAttempt(candidate, endpoint, restriction, activeDuration, end ?? new RecordingEnd.Completed(StreamEndReason.EndOfStream)));
		}

		private Task<CandidateBatch> FetchAsync(CancellationToken cancellationToken)
		{
			string sign = (++Requests).ToString(CultureInfo.InvariantCulture);
			return Task.FromResult(new CandidateBatch(create(sign), new Dictionary<long, string>(), Request));
		}
	}
}
