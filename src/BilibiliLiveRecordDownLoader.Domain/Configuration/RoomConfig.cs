namespace BilibiliLiveRecordDownLoader.Domain.Configuration;

// 同一实例也被房间会话不持锁读取，所以不可变，规范化也返回新实例
public sealed record RoomConfig
{
	public long RoomId { get; init; }

	public bool IsRecordingEnabled { get; init; } = RoomDefaults.IsRecordingEnabled;

	public bool IsNotificationEnabled { get; init; }

	public bool IsDanmakuEnabled { get; init; } = DanmakuDefaults.IsEnabled;

	public TimeSpan? DanmakuOffset { get; init; }

	public StreamQuality Quality { get; init; } = RoomDefaults.Quality;

	public IReadOnlyList<StreamCodec>? CodecPriority { get; init; }

	public IReadOnlyList<StreamFormat>? FormatPriority { get; init; }

	public bool? ConvertToMp4 { get; init; }

	public bool? DeleteAfterConvert { get; init; }

	public RecordingSplitMode? SplitMode { get; init; }

	public TimeSpan? SplitDuration { get; init; }

	public long? SplitSizeMebibytes { get; init; }

	public bool? SplitOnTitleChange { get; init; }

	public TimeSpan StatusCheckInterval { get; init; } = RoomDefaults.StatusCheckInterval;

	public TimeSpan DanmuReconnectDelay { get; init; } = RoomDefaults.DanmuReconnectDelay;

	public TimeSpan StreamReconnectDelay { get; init; } = RoomDefaults.StreamReconnectDelay;

	public TimeSpan StreamConnectTimeout { get; init; } = RoomDefaults.StreamConnectTimeout;

	public TimeSpan StreamIdleTimeout { get; init; } = RoomDefaults.StreamIdleTimeout;

	public RoomConfig Normalize()
	{
		return this with
		{
			Quality = Quality.IsValid ? Quality : RoomDefaults.Quality,
			StatusCheckInterval = ConfigValues.ClampRoomInterval(StatusCheckInterval, RoomDefaults.MinStatusCheckIntervalSeconds),
			DanmuReconnectDelay = ConfigValues.ClampRoomInterval(DanmuReconnectDelay, RoomDefaults.MinDanmuReconnectDelaySeconds),
			StreamReconnectDelay = ConfigValues.ClampRoomInterval(StreamReconnectDelay, RoomDefaults.MinStreamReconnectDelaySeconds),
			StreamConnectTimeout = ConfigValues.ClampRoomInterval(StreamConnectTimeout, RoomDefaults.MinStreamConnectTimeoutSeconds),
			StreamIdleTimeout = ConfigValues.ClampRoomInterval(StreamIdleTimeout, RoomDefaults.MinStreamIdleTimeoutSeconds),
			CodecPriority = NormalizeOverride(CodecPriority),
			FormatPriority = NormalizeOverride(FormatPriority),
			SplitDuration = SplitDuration is { } duration ? ConfigValues.ClampSplitDuration(duration) : null,
			SplitSizeMebibytes = SplitSizeMebibytes is { } size ? ConfigValues.ClampSplitSize(size) : null,
			DanmakuOffset = DanmakuOffset is { } offset ? ConfigValues.ClampDanmakuOffset(offset) : null,
			SplitMode = DefinedOrInherit(SplitMode)
		};

		static T? DefinedOrInherit<T>(T? value) where T : struct, Enum
		{
			return value is { } defined && Enum.IsDefined(defined) ? defined : null;
		}

		static IReadOnlyList<T>? NormalizeOverride<T>(IReadOnlyList<T>? priority) where T : struct, Enum
		{
			IReadOnlyList<T> normalized = StreamPriority.Normalize(priority, []);
			return normalized.Count > 0 ? normalized : null;
		}
	}
}
