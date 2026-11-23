namespace BilibiliLive;

public sealed class BilibiliApiException(string message, long code = 0) : Exception(message)
{
	public const long NotLoggedInCode = -101;
	public const long RiskControlCode = -352;
	public const long RequestBlockedCode = -412;

	public const long RoomNotFoundCode = 60004;

	public long Code { get; } = code;
}
