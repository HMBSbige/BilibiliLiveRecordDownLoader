namespace BilibiliLiveRecordDownLoader.Application.Rooms;

internal static class RoomSettingsMapper
{
	extension(RoomSettingsDto settings)
	{
		public RoomConfig ToConfig()
		{
			return new RoomConfig
			{
				RoomId = settings.RoomId,
				IsRecordingEnabled = settings.IsRecordingEnabled,
				IsNotificationEnabled = settings.IsNotificationEnabled,
				IsDanmakuEnabled = settings.IsDanmakuEnabled,
				DanmakuOffset = settings.DanmakuOffset,
				Quality = settings.Quality,
				CodecPriority = settings.CodecPriority,
				FormatPriority = settings.FormatPriority,
				ConvertToMp4 = settings.ConvertToMp4,
				DeleteAfterConvert = settings.DeleteAfterConvert,
				SplitMode = settings.SplitMode,
				SplitDuration = settings.SplitDuration,
				SplitSizeMebibytes = settings.SplitSizeMebibytes,
				SplitOnTitleChange = settings.SplitOnTitleChange,
				StatusCheckInterval = settings.StatusCheckInterval,
				DanmuReconnectDelay = settings.DanmuReconnectDelay,
				StreamReconnectDelay = settings.StreamReconnectDelay,
				StreamConnectTimeout = settings.StreamConnectTimeout,
				StreamIdleTimeout = settings.StreamIdleTimeout
			}.Normalize();
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
				IsDanmakuEnabled = config.IsDanmakuEnabled,
				DanmakuOffset = config.DanmakuOffset,
				Quality = config.Quality,
				CodecPriority = config.CodecPriority,
				FormatPriority = config.FormatPriority,
				ConvertToMp4 = config.ConvertToMp4,
				DeleteAfterConvert = config.DeleteAfterConvert,
				SplitMode = config.SplitMode,
				SplitDuration = config.SplitDuration,
				SplitSizeMebibytes = config.SplitSizeMebibytes,
				SplitOnTitleChange = config.SplitOnTitleChange,
				StatusCheckInterval = config.StatusCheckInterval,
				DanmuReconnectDelay = config.DanmuReconnectDelay,
				StreamReconnectDelay = config.StreamReconnectDelay,
				StreamConnectTimeout = config.StreamConnectTimeout,
				StreamIdleTimeout = config.StreamIdleTimeout
			};
		}
	}
}
