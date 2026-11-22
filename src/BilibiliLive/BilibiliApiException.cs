namespace BilibiliLive;

public sealed class BilibiliApiException(string message, long code = 0, Exception? innerException = null) : Exception(message, innerException)
{
	public const long NotLoggedInCode = -101;

	public long Code { get; } = code;
}
