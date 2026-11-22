namespace BilibiliLiveRecordDownLoader.Domain.Configuration;

public record RoomConfig
{
	public long RoomId { get; set; }

	public bool IsRecordingEnabled { get; set; } = RoomDefaults.IsRecordingEnabled;

	public bool IsNotificationEnabled { get; set; }

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
		StatusCheckInterval = Max(StatusCheckInterval, RoomDefaults.MinStatusCheckIntervalSeconds);
		DanmuReconnectDelay = Max(DanmuReconnectDelay, RoomDefaults.MinDanmuReconnectDelaySeconds);
		StreamReconnectDelay = Max(StreamReconnectDelay, RoomDefaults.MinStreamReconnectDelaySeconds);
		StreamConnectTimeout = Max(StreamConnectTimeout, RoomDefaults.MinStreamConnectTimeoutSeconds);
		StreamIdleTimeout = Max(StreamIdleTimeout, RoomDefaults.MinStreamIdleTimeoutSeconds);
		CodecPriority = NormalizeOverride(CodecPriority);
		FormatPriority = NormalizeOverride(FormatPriority);
		SplitDuration = SplitDuration is { } duration ? RecordingSplitLimits.ClampDuration(duration) : null;
		SplitSizeMegabytes = SplitSizeMegabytes is { } size ? RecordingSplitLimits.ClampSize(size) : null;

		return;

		static TimeSpan Max(TimeSpan value, decimal minimumSeconds)
		{
			TimeSpan minimum = TimeSpan.FromSeconds((double)minimumSeconds);
			return value < minimum ? minimum : value;
		}

		static List<T>? NormalizeOverride<T>(List<T>? priority) where T : struct, Enum
		{
			List<T> normalized = StreamPriority.Normalize(priority, []);
			return normalized.Count > 0 ? normalized : null;
		}
	}
}
