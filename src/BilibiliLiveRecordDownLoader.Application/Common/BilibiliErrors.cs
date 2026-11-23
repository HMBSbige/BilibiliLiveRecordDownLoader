namespace BilibiliLiveRecordDownLoader.Application.Common;

internal static class BilibiliErrors
{
	public static bool IsBlocked(Exception exception)
	{
		return exception is HttpRequestException { StatusCode: HttpStatusCode.PreconditionFailed } or BilibiliApiException { Code: BilibiliApiException.RequestBlockedCode or BilibiliApiException.RiskControlCode };
	}

	public static string Describe(Exception exception)
	{
		if (IsBlocked(exception))
		{
			return AppLanguage.Current.RequestRateLimited;
		}

		return RequestTimeout.IsTimedOut(exception) ? AppLanguage.Current.RequestTimedOut : exception.Message;
	}

	public static string Describe(Exception exception, long roomId)
	{
		return exception is BilibiliApiException { Code: BilibiliApiException.RoomNotFoundCode } ? AppLanguage.Current.RoomNotFound.ToString(roomId) : Describe(exception);
	}
}
