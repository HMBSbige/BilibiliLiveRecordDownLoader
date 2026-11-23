using BilibiliLive.Streams;
using System.Collections.Concurrent;

namespace BilibiliLive.Tests.Streams;

public class HostSelectorTest
{
	private static readonly Uri[] Hosts = [new("https://a.test/"), new("https://b.test/"), new("https://c.test/")];

	private sealed class FakeSession(Uri uri) : IAsyncDisposable
	{
		public Uri Uri { get; } = uri;

		public bool Disposed { get; private set; }

		public ValueTask DisposeAsync()
		{
			Disposed = true;
			return ValueTask.CompletedTask;
		}
	}

	private sealed class Opener(Func<Uri, CancellationToken, Task<FakeSession>> open)
	{
		public ConcurrentQueue<Uri> Attempts { get; } = new();

		public Task<FakeSession> OpenAsync(Uri uri, CancellationToken cancellationToken)
		{
			Attempts.Enqueue(uri);
			return open(uri, cancellationToken);
		}
	}

	private static Task<FakeSession> Fail(Uri uri)
	{
		return Task.FromException<FakeSession>(new HttpRequestException($"{uri.Host} failed"));
	}

	private static Task<FakeSession> Succeed(Uri uri)
	{
		return Task.FromResult(new FakeSession(uri));
	}

	[Test]
	public async Task FirstResponseTriesInOrderAndStopsAtFirstSuccessAsync(CancellationToken cancellationToken)
	{
		Opener opener = new(static (uri, _) => uri == Hosts[0] ? Fail(uri) : Succeed(uri));

		FakeSession session = await HostSelector.OpenAsync(Hosts, StreamHostSelection.FirstResponse, opener.OpenAsync, Random.Shared, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(session.Uri).IsEqualTo(Hosts[1]);
			await Assert.That(opener.Attempts).IsEquivalentTo(Hosts.Take(2), EqualityComparer<Uri>.Default, CollectionOrdering.Matching);
		}
	}

	[Test]
	[MatrixDataSource]
	public async Task AllFailuresAreAggregatedAsync([Matrix] StreamHostSelection selection, CancellationToken cancellationToken)
	{
		Opener opener = new(static (uri, _) => Fail(uri));

		await Assert.ThrowsAsync<AggregateException>(() => HostSelector.OpenAsync(Hosts, selection, opener.OpenAsync, Random.Shared, cancellationToken))
			.And.Member(static e => e.InnerExceptions, static errors => errors.Count().IsEqualTo(Hosts.Length).And.All(static e => e is HttpRequestException));
		await Assert.That(opener.Attempts).IsEquivalentTo(Hosts, EqualityComparer<Uri>.Default);
	}

	[Test]
	public async Task FastestResponseWinsCancelsAndDisposesTheRestAsync(CancellationToken cancellationToken)
	{
		TaskCompletionSource<FakeSession> slow = new(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource slowCancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
		CancellationToken cancelledToken = default;
		FakeSession lateSession = new(Hosts[0]);

		Opener opener = new(async (uri, ct) =>
		{
			if (uri == Hosts[0])
			{
				ct.Register(slowCancelled.SetResult);
				// Ignores cancellation on purpose, like a request whose response arrives just as it is cancelled
				return await slow.Task;
			}

			if (uri == Hosts[1])
			{
				cancelledToken = ct;
				await Task.Delay(Timeout.Infinite, ct);
			}

			await Task.Yield();
			return new FakeSession(uri);
		});

		Task<FakeSession> opening = HostSelector.OpenAsync(Hosts, StreamHostSelection.FastestResponse, opener.OpenAsync, Random.Shared, cancellationToken);
		await slowCancelled.Task.WaitAsync(cancellationToken);
		bool pendingWhileLoserRuns = !opening.IsCompleted;
		slow.SetResult(lateSession);
		FakeSession winner = await opening;

		using (Assert.Multiple())
		{
			await Assert.That(winner.Uri).IsEqualTo(Hosts[2]);
			await Assert.That(winner.Disposed).IsFalse();
			await Assert.That(cancelledToken.IsCancellationRequested).IsTrue();
			await Assert.That(lateSession.Disposed).IsTrue();
			await Assert.That(pendingWhileLoserRuns).IsTrue();
		}
	}

	[Test]
	public async Task FastestResponseStartsAllAttemptsConcurrentlyAsync(CancellationToken cancellationToken)
	{
		TaskCompletionSource allStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
		int started = 0;

		Opener opener = new(async (uri, ct) =>
		{
			if (Interlocked.Increment(ref started) == Hosts.Length)
			{
				allStarted.SetResult();
			}

			// Each attempt only finishes once all of them are in flight, which sequential opening would never reach
			await allStarted.Task.WaitAsync(ct);
			return uri == Hosts[1] ? new FakeSession(uri) : throw new HttpRequestException("failed");
		});

		FakeSession session = await HostSelector.OpenAsync(Hosts, StreamHostSelection.FastestResponse, opener.OpenAsync, Random.Shared, cancellationToken);

		await Assert.That(session.Uri).IsEqualTo(Hosts[1]);
	}

	[Test]
	[MatrixDataSource]
	public async Task OuterCancellationIsNotReportedAsHostFailureAsync([Matrix] StreamHostSelection selection)
	{
		using CancellationTokenSource cts = new();
		await cts.CancelAsync();
		Opener opener = new(static (_, ct) => Task.FromCanceled<FakeSession>(ct));

		await Assert.ThrowsAsync<OperationCanceledException>(() => HostSelector.OpenAsync(Hosts, selection, opener.OpenAsync, Random.Shared, cts.Token));
	}

	[Test]
	public async Task RandomShufflesThenTriesHostsSequentiallyAsync(CancellationToken cancellationToken)
	{
		Random random = new(20261121);
		HashSet<Uri> firstAttempts = [];

		for (int i = 0; i < 32; ++i)
		{
			Opener opener = new(static (uri, _) => uri == Hosts[2] ? Succeed(uri) : Fail(uri));

			FakeSession session = await HostSelector.OpenAsync(Hosts, StreamHostSelection.Random, opener.OpenAsync, random, cancellationToken);
			List<Uri> attempts = [.. opener.Attempts];

			await Assert.That(session.Uri).IsEqualTo(Hosts[2]);
			await Assert.That(attempts).HasDistinctItems();
			await Assert.That(attempts).LastItem().IsEqualTo(Hosts[2]);
			firstAttempts.Add(attempts[0]);
		}

		await Assert.That(firstAttempts).Count().IsGreaterThan(1);
	}
}
