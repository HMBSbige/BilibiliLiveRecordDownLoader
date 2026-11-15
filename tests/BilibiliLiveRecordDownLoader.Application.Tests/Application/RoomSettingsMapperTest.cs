namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class RoomSettingsMapperTest
{
	[Test]
	public async Task SettingsSurviveRoundTripAsync()
	{
		RoomSettingsDto settings = new()
		{
			RoomId = 732,
			IsRecordingEnabled = false,
			IsNotificationEnabled = true,
			Quality = StreamQuality.UltraHd4K,
			RecorderType = RecorderType.FFmpeg,
			CodecPriority = [StreamCodec.Hevc],
			FormatPriority = [StreamFormat.Ts, StreamFormat.Flv],
			ConvertToMp4 = true,
			DeleteAfterConvert = false,
			StatusCheckInterval = TimeSpan.FromMinutes(1),
			DanmuReconnectDelay = TimeSpan.FromSeconds(3),
			StreamReconnectDelay = TimeSpan.FromSeconds(4),
			StreamConnectTimeout = TimeSpan.FromSeconds(5),
			StreamIdleTimeout = TimeSpan.FromSeconds(6)
		};

		RoomSettingsDto roundTripped = settings.ToConfig().ToDto();

		await Assert.That(roundTripped with { CodecPriority = null, FormatPriority = null }).IsEqualTo(settings with { CodecPriority = null, FormatPriority = null });
		await Assert.That(roundTripped.CodecPriority?.SequenceEqual([StreamCodec.Hevc])).IsTrue();
		await Assert.That(roundTripped.FormatPriority?.SequenceEqual([StreamFormat.Ts, StreamFormat.Flv])).IsTrue();
	}

	[Test]
	public async Task ConversionToConfigAppliesDefaultsAsync()
	{
		RoomConfig config = new RoomSettingsDto { RoomId = 1, StatusCheckInterval = TimeSpan.Zero, CodecPriority = [] }.ToConfig();

		await Assert.That(config.StatusCheckInterval).IsEqualTo(TimeSpan.FromSeconds(10));
		await Assert.That(config.CodecPriority).IsNull();
	}
}
