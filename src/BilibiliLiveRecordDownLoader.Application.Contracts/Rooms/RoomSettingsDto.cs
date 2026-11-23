namespace BilibiliLiveRecordDownLoader.Application.Contracts.Rooms;

public sealed record RoomSettingsDto
{
	public required long RoomId { get; init; }

	public bool IsRecordingEnabled { get; init; } = RoomDefaults.IsRecordingEnabled;

	public bool IsNotificationEnabled { get; init; }

	public bool IsDanmakuEnabled { get; init; } = DanmakuDefaults.IsEnabled;

	// 为 null 时用全局的弹幕时间偏移
	public TimeSpan? DanmakuOffset { get; init; }

	public StreamQuality Quality { get; init; } = RoomDefaults.Quality;

	public RecorderType? RecorderType { get; init; }

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
}
