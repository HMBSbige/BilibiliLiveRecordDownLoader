namespace BilibiliLiveRecordDownLoader.Application.Common;

internal static class BilibiliErrors
{
	public static bool IsBlocked(Exception exception)
	{
		return exception is HttpRequestException { StatusCode: HttpStatusCode.PreconditionFailed } or BilibiliApiException { Code: BilibiliApiException.RequestBlockedCode or BilibiliApiException.RiskControlCode };
	}

	// 被风控拦截时原文只有状态码或错误码
	public static string Describe(Exception exception)
	{
		if (IsBlocked(exception))
		{
			return AppLanguage.Current.RequestRateLimited;
		}

		return RequestTimeout.IsTimedOut(exception) ? AppLanguage.Current.RequestTimedOut : exception.Message;
	}

	// 查询单个房间时，房间不存在的原文是库里写的英文
	public static string Describe(Exception exception, long roomId)
	{
		return exception is BilibiliApiException { Code: BilibiliApiException.RoomNotFoundCode } ? AppLanguage.Current.RoomNotFound.ToString(roomId) : Describe(exception);
	}
}
