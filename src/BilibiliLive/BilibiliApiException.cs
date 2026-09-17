namespace BilibiliLive;

public sealed class BilibiliApiException(string message, long code = 0, Exception? innerException = null) : Exception(message, innerException)
{
	public long Code { get; } = code;
}
