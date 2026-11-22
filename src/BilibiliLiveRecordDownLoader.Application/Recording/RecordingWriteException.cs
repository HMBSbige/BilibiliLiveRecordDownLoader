namespace BilibiliLiveRecordDownLoader.Application.Recording;

// 写盘失败与线路无关，换线路也写不进去
internal sealed class RecordingWriteException(string message, string displayText, Exception innerException) : IOException(message, innerException)
{
	// 通知标题已经说明是写盘失败，DisplayText 只用 IO 错误本身的说明
	public RecordingWriteException(Exception innerException) : this($"Failed to write the recording: {innerException.Message}", innerException.Message, innerException)
	{
	}

	public string DisplayText { get; } = displayText;
}
