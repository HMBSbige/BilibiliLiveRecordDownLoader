namespace BilibiliLiveRecordDownLoader.Application.Contracts.Platform;

public interface IApplicationShell
{
	bool StartMinimized { get; }

	void ShowMainWindow();

	void Shutdown();
}
