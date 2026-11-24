namespace BilibiliLiveRecordDownLoader.Application.Common;

// HttpClient 超时抛的是 TaskCanceledException，界面会把它当成用户取消而不提示
// 只有超时才带着 TimeoutException，不必再看调用方的令牌
internal static class RequestTimeout
{
	public static bool IsTimedOut(Exception exception)
	{
		return exception is OperationCanceledException { InnerException: TimeoutException };
	}

	public static async Task ReportAsync(Task request)
	{
		try
		{
			await request;
		}
		catch (OperationCanceledException exception) when (IsTimedOut(exception))
		{
			throw new TimeoutException(AppLanguage.Current.RequestTimedOut, exception);
		}
	}

	public static async Task<T> ReportAsync<T>(Task<T> request)
	{
		try
		{
			return await request;
		}
		catch (OperationCanceledException exception) when (IsTimedOut(exception))
		{
			throw new TimeoutException(AppLanguage.Current.RequestTimedOut, exception);
		}
	}

	public static async ValueTask<T> ReportAsync<T>(ValueTask<T> request)
	{
		try
		{
			return await request;
		}
		catch (OperationCanceledException exception) when (IsTimedOut(exception))
		{
			throw new TimeoutException(AppLanguage.Current.RequestTimedOut, exception);
		}
	}
}
