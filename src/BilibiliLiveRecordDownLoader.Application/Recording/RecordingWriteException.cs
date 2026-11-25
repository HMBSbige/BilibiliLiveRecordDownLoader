namespace BilibiliLiveRecordDownLoader.Application.Recording;

internal sealed class RecordingWriteException(string message, string displayText, Exception innerException, bool isStalled = false) : IOException(message, innerException)
{
	public RecordingWriteException(Exception innerException) : this($"Failed to write the recording: {innerException.Message}", innerException.Message, innerException)
	{
	}

	public string DisplayText { get; } = displayText;

	public bool IsStalled { get; } = isStalled;
}
