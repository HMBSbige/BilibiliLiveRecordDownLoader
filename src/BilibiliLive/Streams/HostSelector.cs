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
		// ReSharper disable once AccessToDisposedClosure
		Task<T>[] attempts = [.. urls.Select(uri => open(uri, losers.Token))];

		T? winner = null;
		List<Exception> errors = [];

		// 等完每个尝试，晚到但仍打开成功的落选者才会被释放而不是泄漏
		await foreach (Task<T> attempt in Task.WhenEach(attempts))
		{
			if (attempt.IsCompletedSuccessfully)
			{
				T session = await attempt;

				if (winner is null)
				{
					winner = session;
					await losers.CancelAsync();
				}
				else
				{
					await session.DisposeAsync();
				}
			}
			// 读取 Exception 同时把异常标记为已观察，赢家产生之后才失败的尝试靠它避免留下未观察的异常
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
