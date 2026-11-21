namespace BilibiliLiveRecordDownLoader.Application.Recording;

internal interface IRecordingObserver
{
	void OnRecordingStarted(string path, LiveStreamCandidate stream);

	void OnSpeedChanged(double bytesPerSecond);
}
