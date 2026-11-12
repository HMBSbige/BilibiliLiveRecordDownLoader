namespace BilibiliLiveRecordDownLoader.Application.Rooms;

internal static class RoomSettingsMapper
{
	extension(RoomSettingsDto settings)
	{
		public RoomConfig ToConfig()
		{
			RoomConfig config = new()
			{
				RoomId = settings.RoomId,
				IsRecordingEnabled = settings.IsRecordingEnabled,
				IsNotificationEnabled = settings.IsNotificationEnabled,
				Quality = settings.Quality,
				RecorderType = settings.RecorderType,
				CodecPriority = settings.CodecPriority is { } codecs ? [.. codecs] : null,
				FormatPriority = settings.FormatPriority is { } formats ? [.. formats] : null,
				ConvertToMp4 = settings.ConvertToMp4,
				DeleteAfterConvert = settings.DeleteAfterConvert,
				StatusCheckInterval = settings.StatusCheckInterval,
				DanmuReconnectDelay = settings.DanmuReconnectDelay,
				StreamReconnectDelay = settings.StreamReconnectDelay,
				StreamConnectTimeout = settings.StreamConnectTimeout,
				StreamIdleTimeout = settings.StreamIdleTimeout
			};
			config.ApplyDefaults();
			return config;
		}
	}

	extension(RoomConfig config)
	{
		public RoomSettingsDto ToDto()
		{
			return new RoomSettingsDto
			{
				RoomId = config.RoomId,
				IsRecordingEnabled = config.IsRecordingEnabled,
				IsNotificationEnabled = config.IsNotificationEnabled,
				Quality = config.Quality,
				RecorderType = config.RecorderType,
				CodecPriority = config.CodecPriority,
				FormatPriority = config.FormatPriority,
				ConvertToMp4 = config.ConvertToMp4,
				DeleteAfterConvert = config.DeleteAfterConvert,
				StatusCheckInterval = config.StatusCheckInterval,
				DanmuReconnectDelay = config.DanmuReconnectDelay,
				StreamReconnectDelay = config.StreamReconnectDelay,
				StreamConnectTimeout = config.StreamConnectTimeout,
				StreamIdleTimeout = config.StreamIdleTimeout
			};
		}
	}
}
