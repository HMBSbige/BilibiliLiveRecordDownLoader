namespace BilibiliLiveRecordDownLoader.Application.Recording;

internal interface IRecordingObserver
{
	void OnRecordingStarted(string path, RecordingStream stream);

	// 录制中分段时，前一段写完并关闭，接着开始下一段；最后一段仍随录制尝试结束
	void OnSegmentCompleted(string path);

	void OnSegmentStarted(string path);

	void OnSpeedChanged(double bytesPerSecond);
}
