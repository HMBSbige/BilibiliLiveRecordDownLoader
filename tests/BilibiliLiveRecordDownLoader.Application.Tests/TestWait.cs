namespace BilibiliLiveRecordDownLoader.Application.Tests;

// 被测代码按假时钟计时，这里只能用真实时间
internal static class TestWait
{
	public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

	public static Task UntilAsync(Func<bool> condition, string because, CancellationToken cancellationToken, TimeSpan? timeout = null)
	{
		return ForAsync(async token =>
		{
			using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(10));

			while (!condition())
			{
				await timer.WaitForNextTickAsync(token);
			}

			return true;
		}, because, cancellationToken, timeout);
	}

	public static Task ForAsync(Task task, string because, CancellationToken cancellationToken, TimeSpan? timeout = null)
	{
		return ForAsync(async token =>
		{
			await task.WaitAsync(token);
			return true;
		}, because, cancellationToken, timeout);
	}

	// 超时要取消 wait，否则读取通道这类等待会留下来吞掉之后的数据
	public static async Task<T> ForAsync<T>(Func<CancellationToken, ValueTask<T>> wait, string because, CancellationToken cancellationToken, TimeSpan? timeout = null)
	{
		using CancellationTokenSource source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		source.CancelAfter(timeout ?? DefaultTimeout);

		try
		{
			return await wait(source.Token);
		}
		catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
		{
			throw new TimeoutException($"Timed out waiting until {because}.", exception);
		}
	}
}
