namespace BilibiliLiveRecordDownLoader.Application.Contracts.Lifecycle;

public interface IAppLifecycleService
{
	Task StartAsync(CancellationToken cancellationToken = default);

	Task StopAsync();

	void SetUseSystemProxy(bool enabled);
}
