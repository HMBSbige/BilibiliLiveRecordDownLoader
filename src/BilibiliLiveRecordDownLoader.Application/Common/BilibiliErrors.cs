namespace BilibiliLiveRecordDownLoader.Application.Common;

internal static class BilibiliErrors
{
	private const long RequestBlockedCode = -412;
	private const long RiskControlCode = -352;

	public static bool IsBlocked(Exception exception)
	{
		return exception is HttpRequestException { StatusCode: HttpStatusCode.PreconditionFailed } or BilibiliApiException { Code: RequestBlockedCode or RiskControlCode };
	}

	// 被风控拦截时原文只有状态码或错误码
	public static string Describe(Exception exception)
	{
		return IsBlocked(exception) ? AppLanguage.Current.RequestRateLimited : exception.Message;
	}
}
