namespace BilibiliLiveRecordDownLoader.Application.Recording;

// IsStreamerSilent：主播端没在推流（接口说直播中却没给能录的流，或者线路都说没有这路流），不提醒
internal sealed class StreamUnavailableException(string message, string displayText, bool isStreamerSilent) : HttpRequestException(message)
{
	public string DisplayText { get; } = displayText;

	public bool IsStreamerSilent { get; } = isStreamerSilent;
}
