namespace BilibiliLiveRecordDownLoader.Application.Recording;

// 写盘失败与线路无关，换线路也写不进去
internal sealed class RecordingWriteException(string message, Exception innerException) : IOException(message, innerException)
{
	public RecordingWriteException(Exception innerException) : this($"Failed to write the recording: {innerException.Message}", innerException)
	{
	}
}
