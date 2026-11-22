namespace BilibiliLiveRecordDownLoader.Application.Lifecycle;

internal sealed partial class AppLifecycleService(IAppConfigManager configManager, SwitchableWebProxy proxy, RoomAppService roomAppService, BackgroundTaskAppService backgroundTaskAppService, RecordingPostProcessor postProcessor, IUpdateAppService updateAppService, ILogger<AppLifecycleService> logger) : IAppLifecycleService
{
	public async Task StartAsync(CancellationToken cancellationToken = default)
	{
		AppConfig config = await configManager.GetAsync(cancellationToken);
		proxy.Enabled = config.UseSystemProxy;
		LogStarting(logger, updateAppService.CurrentVersion, config.Rooms.Count);

		await roomAppService.StartAsync(cancellationToken);
	}

	public async Task StopAsync()
	{
		// 先让任务队列进入停止状态，录制收尾时排入的转封装就不会再启动
		Task tasks = backgroundTaskAppService.StopAsync();
		await Task.WhenAll(roomAppService.StopAsync(), tasks);

		// 房间都停了之后最后几段的后处理才都已登记
		await postProcessor.WhenIdleAsync();
	}

	public void SetUseSystemProxy(bool enabled)
	{
		proxy.Enabled = enabled;
	}

	[LoggerMessage(Level = LogLevel.Information, Message = "Starting {Version} with {RoomCount} rooms")]
	private static partial void LogStarting(ILogger logger, string version, int roomCount);
}
