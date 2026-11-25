using BilibiliLiveRecordDownLoader.Domain.Shared.Recording;

namespace BilibiliLiveRecordDownLoader.Domain.Shared.Configuration;

public static class RoomDefaults
{
	public const bool IsRecordingEnabled = true;

	public static StreamQuality Quality => StreamQuality.Original;

	public const decimal MinStatusCheckIntervalSeconds = 10;

	public const decimal MinDanmuReconnectDelaySeconds = 1;

	public const decimal MinStreamReconnectDelaySeconds = 1;

	public const decimal MinStreamConnectTimeoutSeconds = 1;

	public const decimal MinStreamIdleTimeoutSeconds = 3;

	// 太大时定时器和等待会抛异常
	public const decimal MaxIntervalSeconds = 24 * 60 * 60;

	public static TimeSpan MaxInterval { get; } = TimeSpan.FromSeconds((double)MaxIntervalSeconds);

	public static TimeSpan StatusCheckInterval { get; } = TimeSpan.FromMinutes(5);

	public static TimeSpan DanmuReconnectDelay { get; } = TimeSpan.FromSeconds(2);

	public static TimeSpan StreamReconnectDelay { get; } = TimeSpan.FromSeconds(6);

	public static TimeSpan StreamConnectTimeout { get; } = TimeSpan.FromSeconds(5);

	public static TimeSpan StreamIdleTimeout { get; } = TimeSpan.FromSeconds(10);
}
