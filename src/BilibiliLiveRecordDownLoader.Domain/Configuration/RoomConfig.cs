namespace BilibiliLiveRecordDownLoader.Domain.Configuration;

public record RoomConfig
{
	public long RoomId { get; set; }

	public bool IsRecordingEnabled { get; set; } = RoomDefaults.IsRecordingEnabled;

	public bool IsNotificationEnabled { get; set; }

	public bool IsDanmakuEnabled { get; set; } = DanmakuDefaults.IsEnabled;

	public TimeSpan? DanmakuOffset { get; set; }

	public StreamQuality Quality { get; set; } = RoomDefaults.Quality;

	public RecorderType? RecorderType { get; set; }

	public List<StreamCodec>? CodecPriority { get; set; }

	public List<StreamFormat>? FormatPriority { get; set; }

	public bool? ConvertToMp4 { get; set; }

	public bool? DeleteAfterConvert { get; set; }

	public RecordingSplitMode? SplitMode { get; set; }

	public TimeSpan? SplitDuration { get; set; }

	public long? SplitSizeMegabytes { get; set; }

	public bool? SplitOnTitleChange { get; set; }

	public TimeSpan StatusCheckInterval { get; set; } = RoomDefaults.StatusCheckInterval;

	public TimeSpan DanmuReconnectDelay { get; set; } = RoomDefaults.DanmuReconnectDelay;

	public TimeSpan StreamReconnectDelay { get; set; } = RoomDefaults.StreamReconnectDelay;

	public TimeSpan StreamConnectTimeout { get; set; } = RoomDefaults.StreamConnectTimeout;

	public TimeSpan StreamIdleTimeout { get; set; } = RoomDefaults.StreamIdleTimeout;

	public void ApplyDefaults()
	{
		StatusCheckInterval = ConfigValues.ClampRoomInterval(StatusCheckInterval, RoomDefaults.MinStatusCheckIntervalSeconds);
		DanmuReconnectDelay = ConfigValues.ClampRoomInterval(DanmuReconnectDelay, RoomDefaults.MinDanmuReconnectDelaySeconds);
		StreamReconnectDelay = ConfigValues.ClampRoomInterval(StreamReconnectDelay, RoomDefaults.MinStreamReconnectDelaySeconds);
		StreamConnectTimeout = ConfigValues.ClampRoomInterval(StreamConnectTimeout, RoomDefaults.MinStreamConnectTimeoutSeconds);
		StreamIdleTimeout = ConfigValues.ClampRoomInterval(StreamIdleTimeout, RoomDefaults.MinStreamIdleTimeoutSeconds);
		CodecPriority = NormalizeOverride(CodecPriority);
		FormatPriority = NormalizeOverride(FormatPriority);
		SplitDuration = SplitDuration is { } duration ? ConfigValues.ClampSplitDuration(duration) : null;
		SplitSizeMegabytes = SplitSizeMegabytes is { } size ? ConfigValues.ClampSplitSize(size) : null;
		DanmakuOffset = DanmakuOffset is { } offset ? ConfigValues.ClampDanmakuOffset(offset) : null;
		// 枚举在配置里也能写成数字；覆盖值无效时和没覆盖一样继承全局设置
		RecorderType = DefinedOrInherit(RecorderType);
		SplitMode = DefinedOrInherit(SplitMode);

		return;

		static T? DefinedOrInherit<T>(T? value) where T : struct, Enum
		{
			return value is { } defined && Enum.IsDefined(defined) ? defined : null;
		}

		static List<T>? NormalizeOverride<T>(List<T>? priority) where T : struct, Enum
		{
			List<T> normalized = StreamPriority.Normalize(priority, []);
			return normalized.Count > 0 ? normalized : null;
		}
	}
}
