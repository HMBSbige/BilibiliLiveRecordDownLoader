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
		StatusCheckInterval = Clamp(StatusCheckInterval, RoomDefaults.MinStatusCheckIntervalSeconds);
		DanmuReconnectDelay = Clamp(DanmuReconnectDelay, RoomDefaults.MinDanmuReconnectDelaySeconds);
		StreamReconnectDelay = Clamp(StreamReconnectDelay, RoomDefaults.MinStreamReconnectDelaySeconds);
		StreamConnectTimeout = Clamp(StreamConnectTimeout, RoomDefaults.MinStreamConnectTimeoutSeconds);
		StreamIdleTimeout = Clamp(StreamIdleTimeout, RoomDefaults.MinStreamIdleTimeoutSeconds);
		CodecPriority = NormalizeOverride(CodecPriority);
		FormatPriority = NormalizeOverride(FormatPriority);
		SplitDuration = SplitDuration is { } duration ? RecordingSplitLimits.ClampDuration(duration) : null;
		SplitSizeMegabytes = SplitSizeMegabytes is { } size ? RecordingSplitLimits.ClampSize(size) : null;
		DanmakuOffset = DanmakuOffset is { } offset ? DanmakuDefaults.ClampOffset(offset) : null;
		// 枚举在配置里也能写成数字；覆盖值无效时和没覆盖一样继承全局设置
		RecorderType = DefinedOrInherit(RecorderType);
		SplitMode = DefinedOrInherit(SplitMode);

		return;

		static TimeSpan Clamp(TimeSpan value, decimal minimumSeconds)
		{
			return TimeSpan.FromTicks(Math.Clamp(value.Ticks, TimeSpan.FromSeconds((double)minimumSeconds).Ticks, TimeSpan.FromSeconds((double)RoomDefaults.MaxIntervalSeconds).Ticks));
		}

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
