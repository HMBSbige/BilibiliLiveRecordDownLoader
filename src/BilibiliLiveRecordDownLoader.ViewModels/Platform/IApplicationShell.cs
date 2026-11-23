namespace BilibiliLiveRecordDownLoader.ViewModels.Platform;

public interface IApplicationShell
{
	bool StartMinimized { get; }

	void ShowMainWindow();

	// 用户确认退出之后、停止录制之前调用
	void BeginShutdown();

	void Shutdown();
}
