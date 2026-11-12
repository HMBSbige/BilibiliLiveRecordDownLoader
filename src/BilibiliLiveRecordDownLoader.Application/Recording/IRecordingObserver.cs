namespace BilibiliLiveRecordDownLoader.Application.Recording;

internal interface IRecordingObserver
{
	void OnRecordingStarted(string path);

	void OnSpeedChanged(double bytesPerSecond);
}
