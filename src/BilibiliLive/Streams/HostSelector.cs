namespace BilibiliLive.Streams;

internal static class HostSelector
{
	/// <param name="open">第二个参数是其他主机，按尝试顺序从这个主机之后轮转</param>
	public static async Task<T> OpenAsync<T>(IReadOnlyList<Uri> urls, StreamHostSelection selection, Func<Uri, IReadOnlyList<Uri>, CancellationToken, Task<T>> open, Random random, CancellationToken cancellationToken) where T : class, IAsyncDisposable
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

	// 依次打开时，排在打开的主机之前的都打开失败过，轮转之后排到最后；接口可能重复列出同一主机，每个只试一次
	private static List<Uri> GetFallbacks(IReadOnlyList<Uri> order, int opened)
	{
		List<Uri> fallbacks = new(order.Count - 1);

		for (int i = 1; i < order.Count; ++i)
		{
			Uri uri = order[(opened + i) % order.Count];

			if (uri != order[opened] && !fallbacks.Contains(uri))
			{
				fallbacks.Add(uri);
			}
		}

		return fallbacks;
	}

	private static Uri[] Shuffle(IReadOnlyList<Uri> urls, Random random)
	{
		Uri[] shuffled = [.. urls];
		random.Shuffle(shuffled);
		return shuffled;
	}

	private static async Task<T> OpenSequentiallyAsync<T>(IReadOnlyList<Uri> order, Func<Uri, IReadOnlyList<Uri>, CancellationToken, Task<T>> open, CancellationToken cancellationToken) where T : class, IAsyncDisposable
	{
		List<Exception> errors = [];

		for (int i = 0; i < order.Count; ++i)
		{
			try
			{
				return await open(order[i], GetFallbacks(order, i), cancellationToken);
			}
			catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
			{
				errors.Add(ex);
			}
		}

		throw new AggregateException("None of the stream hosts could be opened.", errors);
	}

	private static async Task<T> OpenFastestAsync<T>(IReadOnlyList<Uri> urls, Func<Uri, IReadOnlyList<Uri>, CancellationToken, Task<T>> open, CancellationToken cancellationToken) where T : class, IAsyncDisposable
	{
		using CancellationTokenSource losers = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		// ReSharper disable once AccessToDisposedClosure
		Task<T>[] attempts = [.. urls.Select((uri, i) => open(uri, GetFallbacks(urls, i), losers.Token))];

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
