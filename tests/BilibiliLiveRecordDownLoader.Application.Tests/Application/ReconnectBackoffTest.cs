namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class ReconnectBackoffTest
{
	private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(2);

	private static IEnumerable<TimeSpan> Fail(ReconnectBackoff backoff, int count)
	{
		for (int i = 0; i < count; ++i)
		{
			backoff.OnConnecting();
			yield return backoff.NextDelay();
		}
	}

	[Test]
	[Arguments(2.0, new[] { 2.0, 4, 8, 16, 32, 64, 120, 120 }, DisplayName = "初始 2 秒，翻倍到 2 分钟的上限")]
	[Arguments(360.0, new[] { 360.0, 360, 360 }, DisplayName = "初始间隔超过上限时不缩短")]
	public async Task ConsecutiveFailuresDoubleTheDelayUpToTheCapAsync(double initialSeconds, double[] expectedSeconds)
	{
		ReconnectBackoff backoff = new(() => TimeSpan.FromSeconds(initialSeconds), new FakeTimeProvider(), new FixedRandom(0.5));

		await Assert.That(Fail(backoff, expectedSeconds.Length).Select(static delay => delay.TotalSeconds)).IsEquivalentTo(expectedSeconds, CollectionOrdering.Matching);
	}

	[Test]
	[Arguments(0.0, 1.6)]
	[Arguments(1.0, 2.4)]
	public async Task JitterStaysWithinTwentyPercentAsync(double sample, double seconds)
	{
		ReconnectBackoff backoff = new(static () => InitialDelay, new FakeTimeProvider(), new FixedRandom(sample));
		backoff.OnConnecting();

		await Assert.That(backoff.NextDelay().TotalSeconds).IsEqualTo(seconds).Within(1e-9);
	}

	[Test]
	[Arguments(29, 8)]
	[Arguments(30, 2)]
	public async Task OnlyAConnectionThatLastedTheStableDurationResetsTheDelayAsync(int connectedSeconds, int expectedSeconds)
	{
		FakeTimeProvider time = new();
		ReconnectBackoff backoff = new(static () => InitialDelay, time, new FixedRandom(0.5));
		_ = Fail(backoff, 2).ToArray();

		backoff.OnConnecting();
		time.Advance(TimeSpan.FromSeconds(connectedSeconds));

		await Assert.That(backoff.NextDelay()).IsEqualTo(TimeSpan.FromSeconds(expectedSeconds));
	}

	[Test]
	public async Task ReceivingAMessageResetsTheDelayAsync()
	{
		ReconnectBackoff backoff = new(static () => InitialDelay, new FakeTimeProvider(), new FixedRandom(0.5));
		_ = Fail(backoff, 3).ToArray();

		backoff.OnConnecting();
		backoff.OnReceived();
		TimeSpan afterMessage = backoff.NextDelay();
		TimeSpan next = Fail(backoff, 1).Single();

		await Assert.That((afterMessage, next)).IsEqualTo((InitialDelay, InitialDelay * 2));
	}

	[Test]
	public async Task ChangedInitialDelayAppliesToTheNextDelayAsync()
	{
		TimeSpan initial = InitialDelay;
		ReconnectBackoff backoff = new(() => initial, new FakeTimeProvider(), new FixedRandom(0.5));
		_ = Fail(backoff, 6).ToArray();

		// 已经退避到 2 分钟的上限
		initial = TimeSpan.FromMinutes(5);
		TimeSpan raised = Fail(backoff, 1).Single();
		initial = InitialDelay;
		TimeSpan capped = Fail(backoff, 1).Single();
		initial = TimeSpan.FromSeconds(1);
		backoff.OnConnecting();
		backoff.OnReceived();
		TimeSpan[] lowered = [backoff.NextDelay(), .. Fail(backoff, 2)];

		using (Assert.Multiple())
		{
			await Assert.That(raised).IsEqualTo(TimeSpan.FromMinutes(5));
			await Assert.That(capped).IsEqualTo(ReconnectBackoff.MaxDelay);
			await Assert.That(lowered.Select(static delay => delay.TotalSeconds)).IsEquivalentTo([1.0, 2, 4], CollectionOrdering.Matching);
		}
	}

	private sealed class FixedRandom(double sample) : Random
	{
		public override double NextDouble()
		{
			return sample;
		}
	}
}
