namespace BilibiliLiveRecordDownLoader.Application.Rooms;

internal sealed record RoomSessionServices
(
	BilibiliApiClient ApiClient,
	DanmuClient DanmuClient,
	LiveRecorder Recorder,
	RecordingPostProcessor PostProcessor,
	LowDiskSpaceMonitor DiskSpaceMonitor,
	INotificationService Notifications,
	IAppConfigManager ConfigManager,
	TimeProvider TimeProvider,
	ILogger<RoomSession> Logger
)
{
	// 测试替换成假的弹幕来源
	public Func<long, DanmuReceiveOptions, CancellationToken, IAsyncEnumerable<DanmuMessage>> ReceiveDanmu { get; init; } = DanmuClient.ReceiveAsync;
}

internal sealed class RoomSessionFactory(BilibiliApiClient apiClient, DanmuClient danmuClient, LiveRecorder recorder, RecordingPostProcessor postProcessor, LowDiskSpaceMonitor diskSpaceMonitor, INotificationService notifications, IAppConfigManager configManager, TimeProvider timeProvider, ILogger<RoomSession> logger)
{
	private readonly RoomSessionServices _services = new(apiClient, danmuClient, recorder, postProcessor, diskSpaceMonitor, notifications, configManager, timeProvider, logger);

	public RoomSession Create(RoomConfig settings, Action onChanged)
	{
		return new RoomSession(settings, _services, onChanged);
	}
}
