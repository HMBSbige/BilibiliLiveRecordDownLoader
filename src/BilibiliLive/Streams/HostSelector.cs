namespace BilibiliLive.Streams;

internal static class HostSelector
{
	public static async Task<T> OpenAsync<T>(IReadOnlyList<Uri> urls, StreamHostSelection selection, Func<Uri, CancellationToken, Task<T>> open, Random random, CancellationToken cancellationToken) where T : class, IAsyncDisposable
	{
		if (urls.Count is 0)
		{
			throw new ArgumentException("The stream candidate has no url.", nameof(urls));
		}

		return selection switch
		{
			StreamHostSelection.FirstResponse => await OpenSequentiallyAsync(urls, open, cancellationToken),
			StreamHostSelection.Random => await OpenSequentiallyAsync(Shuffle(urls, random), open, cancellationToken),
			StreamHostSelection.FastestResponse => await OpenFastestAsync(urls, open, cancellationToken),
			_ => throw new ArgumentOutOfRangeException(nameof(selection), selection, "Unknown host selection.")
		};
	}

	private static Uri[] Shuffle(IReadOnlyList<Uri> urls, Random random)
	{
		Uri[] shuffled = [.. urls];
		random.Shuffle(shuffled);
		return shuffled;
	}

	private static async Task<T> OpenSequentiallyAsync<T>(IReadOnlyList<Uri> urls, Func<Uri, CancellationToken, Task<T>> open, CancellationToken cancellationToken) where T : class, IAsyncDisposable
	{
		List<Exception> errors = [];

		foreach (Uri uri in urls)
		{
			try
			{
				return await open(uri, cancellationToken);
			}
			catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
			{
				errors.Add(ex);
			}
		}

		throw new AggregateException("None of the stream hosts could be opened.", errors);
	}

	private static async Task<T> OpenFastestAsync<T>(IReadOnlyList<Uri> urls, Func<Uri, CancellationToken, Task<T>> open, CancellationToken cancellationToken) where T : class, IAsyncDisposable
	{
		using CancellationTokenSource losers = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		Task<T>[] attempts = [.. urls.Select(uri => open(uri, losers.Token))];

		T? winner = null;
		List<Exception> errors = [];

		// Waiting for every attempt guarantees that a loser which still managed to open is disposed rather than leaked
		await foreach (Task<T> attempt in Task.WhenEach(attempts))
		{
			if (attempt.IsCompletedSuccessfully)
			{
				if (winner is null)
				{
					winner = attempt.Result;
					await losers.CancelAsync();
				}
				else
				{
					await attempt.Result.DisposeAsync();
				}
			}
			// Reading Exception also marks it observed, which matters for failures that arrive after a winner
			else if (attempt.Exception?.InnerException is { } error && winner is null)
			{
				errors.Add(error);
			}
		}

		if (winner is not null)
		{
			return winner;
		}

		cancellationToken.ThrowIfCancellationRequested();
		throw new AggregateException("None of the stream hosts could be opened.", errors);
	}
}
