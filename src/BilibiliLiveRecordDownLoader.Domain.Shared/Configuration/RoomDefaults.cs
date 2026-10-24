using BilibiliLiveRecordDownLoader.Domain.Shared.Recording;

namespace BilibiliLiveRecordDownLoader.Domain.Shared.Configuration;

public static class RoomDefaults
{
	public const bool IsRecordingEnabled = true;

	public const StreamQuality Quality = StreamQuality.Original;

	// 下限以秒表示，界面的数值输入框直接引用
	public const decimal MinStatusCheckIntervalSeconds = 10;

	public const decimal MinDanmuReconnectDelaySeconds = 1;

	public const decimal MinStreamReconnectDelaySeconds = 1;

	public const decimal MinStreamConnectTimeoutSeconds = 1;

	public const decimal MinStreamIdleTimeoutSeconds = 3;

	public static TimeSpan StatusCheckInterval { get; } = TimeSpan.FromMinutes(5);

	public static TimeSpan DanmuReconnectDelay { get; } = TimeSpan.FromSeconds(2);

	public static TimeSpan StreamReconnectDelay { get; } = TimeSpan.FromSeconds(6);

	public static TimeSpan StreamConnectTimeout { get; } = TimeSpan.FromSeconds(5);

	public static TimeSpan StreamIdleTimeout { get; } = TimeSpan.FromSeconds(10);
}
