namespace BilibiliLiveRecordDownLoader.Application.Recording;

internal sealed class StreamUnavailableException(string message, string displayText, bool isStreamerSilent) : HttpRequestException(message)
{
	public string DisplayText { get; } = displayText;

	public bool IsStreamerSilent { get; } = isStreamerSilent;
}
