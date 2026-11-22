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
	public async Task ConsecutiveFailuresDoubleTheDelayUpToTheCapAsync()
	{
		ReconnectBackoff backoff = new(InitialDelay, new FakeTimeProvider(), new FixedRandom(0.5));

		TimeSpan[] delays = [.. Fail(backoff, 8)];

		await Assert.That(delays.Select(static delay => delay.TotalSeconds).SequenceEqual([2, 4, 8, 16, 32, 64, 120, 120])).IsTrue();
	}

	[Test]
	[Arguments(0.0, 1.6)]
	[Arguments(1.0, 2.4)]
	public async Task JitterStaysWithinTwentyPercentAsync(double sample, double seconds)
	{
		ReconnectBackoff backoff = new(InitialDelay, new FakeTimeProvider(), new FixedRandom(sample));
		backoff.OnConnecting();

		await Assert.That(backoff.NextDelay().TotalSeconds).IsEqualTo(seconds).Within(1e-9);
	}

	[Test]
	[Arguments(29, 8)]
	[Arguments(30, 2)]
	public async Task OnlyAConnectionThatLastedTheStableDurationResetsTheDelayAsync(int connectedSeconds, int expectedSeconds)
	{
		FakeTimeProvider time = new();
		ReconnectBackoff backoff = new(InitialDelay, time, new FixedRandom(0.5));
		_ = Fail(backoff, 2).ToArray();

		backoff.OnConnecting();
		time.Advance(TimeSpan.FromSeconds(connectedSeconds));

		await Assert.That(backoff.NextDelay()).IsEqualTo(TimeSpan.FromSeconds(expectedSeconds));
	}

	[Test]
	public async Task ReceivingAMessageResetsTheDelayAsync()
	{
		ReconnectBackoff backoff = new(InitialDelay, new FakeTimeProvider(), new FixedRandom(0.5));
		_ = Fail(backoff, 3).ToArray();

		backoff.OnConnecting();
		backoff.OnReceived();
		TimeSpan afterMessage = backoff.NextDelay();
		TimeSpan next = Fail(backoff, 1).Single();

		using (Assert.Multiple())
		{
			await Assert.That(afterMessage).IsEqualTo(InitialDelay);
			await Assert.That(next).IsEqualTo(InitialDelay * 2);
		}
	}

	[Test]
	public async Task InitialDelayAboveTheCapIsNotShortenedAsync()
	{
		TimeSpan initial = ReconnectBackoff.MaxDelay * 3;
		ReconnectBackoff backoff = new(initial, new FakeTimeProvider(), new FixedRandom(0.5));

		await Assert.That(Fail(backoff, 3).All(delay => delay == initial)).IsTrue();
	}

	private sealed class FixedRandom(double sample) : Random
	{
		public override double NextDouble()
		{
			return sample;
		}
	}
}
