namespace BilibiliLiveRecordDownLoader.Application.Rooms;

internal sealed record RoomSessionServices
(
	BilibiliApiClient ApiClient,
	DanmuClient DanmuClient,
	LiveRecorder Recorder,
	RecordingPostProcessor PostProcessor,
	INotificationService Notifications,
	IAppConfigManager ConfigManager,
	TimeProvider TimeProvider,
	ILogger<RoomSession> Logger
);

internal sealed class RoomSessionFactory(BilibiliApiClient apiClient, DanmuClient danmuClient, LiveRecorder recorder, RecordingPostProcessor postProcessor, INotificationService notifications, IAppConfigManager configManager, TimeProvider timeProvider, ILogger<RoomSession> logger)
{
	private readonly RoomSessionServices _services = new(apiClient, danmuClient, recorder, postProcessor, notifications, configManager, timeProvider, logger);

	public RoomSession Create(RoomConfig settings, Action onChanged)
	{
		return new RoomSession(settings, _services, onChanged);
	}
}
