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
			IsDanmakuEnabled = true,
			DanmakuOffset = TimeSpan.FromSeconds(-1.5),
			Quality = StreamQuality.UltraHd4K,
			RecorderType = RecorderType.FFmpeg,
			CodecPriority = [StreamCodec.Hevc],
			FormatPriority = [StreamFormat.Ts, StreamFormat.Flv],
			ConvertToMp4 = true,
			DeleteAfterConvert = false,
			SplitMode = RecordingSplitMode.Size,
			SplitDuration = TimeSpan.FromMinutes(30),
			SplitSizeMegabytes = 1024,
			SplitOnTitleChange = true,
			StatusCheckInterval = TimeSpan.FromMinutes(1),
			DanmuReconnectDelay = TimeSpan.FromSeconds(3),
			StreamReconnectDelay = TimeSpan.FromSeconds(4),
			StreamConnectTimeout = TimeSpan.FromSeconds(5),
			StreamIdleTimeout = TimeSpan.FromSeconds(6)
		};

		await Assert.That(settings.ToConfig().ToDto()).IsEquivalentTo(settings);
	}

	[Test]
	public async Task ConversionToConfigAppliesDefaultsAsync()
	{
		RoomConfig config = new RoomSettingsDto { RoomId = 1, StatusCheckInterval = TimeSpan.Zero, CodecPriority = [] }.ToConfig();

		await Assert.That((config.StatusCheckInterval, config.CodecPriority)).IsEqualTo((TimeSpan.FromSeconds(10), null));
	}
}
