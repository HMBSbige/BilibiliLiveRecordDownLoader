namespace BilibiliLiveRecordDownLoader.Application.Tests;

// 超时时报出在等什么，不必拖满用例的 Timeout 才失败
internal static class TestWait
{
	public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

	public static async Task UntilAsync(Func<bool> condition, string because, CancellationToken cancellationToken, TimeSpan? timeout = null)
	{
		using CancellationTokenSource wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		wait.CancelAfter(timeout ?? DefaultTimeout);

		try
		{
			while (!condition())
			{
				await Task.Delay(10, wait.Token);
			}
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			throw new TimeoutException($"Timed out waiting until {because}.");
		}
	}
}
