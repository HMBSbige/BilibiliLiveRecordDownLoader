namespace BilibiliLiveRecordDownLoader.Application.Lifecycle;

internal sealed partial class AppLifecycleService(IAppConfigManager configManager, SwitchableWebProxy proxy, RoomAppService roomAppService, BackgroundTaskAppService backgroundTaskAppService, RecordingPostProcessor postProcessor, ILogger<AppLifecycleService> logger) : IAppLifecycleService
{
	public async Task StartAsync(CancellationToken cancellationToken = default)
	{
		AppConfig config = await configManager.GetAsync(cancellationToken);
		proxy.Follow(config);
		LogStarting(logger, AppVersion.Current, config.Rooms.Count);

		await roomAppService.StartAsync(cancellationToken);
	}

	public async Task StopAsync()
	{
		// 先让任务队列进入停止状态，录制收尾时排入的转封装就不会再启动
		Task tasks = backgroundTaskAppService.StopAsync();

		try
		{
			await Task.WhenAll(roomAppService.StopAsync(), tasks);
		}
		finally
		{
			// 房间都停了之后最后几段的后处理才都已登记；有房间停止出错时，其余房间的后处理也要等它写完
			await postProcessor.WhenIdleAsync();
		}
	}

	[LoggerMessage(Level = LogLevel.Information, Message = "Starting {Version} with {RoomCount} rooms")]
	private static partial void LogStarting(ILogger logger, string version, int roomCount);
}
