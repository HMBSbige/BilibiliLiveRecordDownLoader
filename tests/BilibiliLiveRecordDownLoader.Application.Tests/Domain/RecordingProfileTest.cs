namespace BilibiliLiveRecordDownLoader.Application.Tests.Domain;

public sealed class RecordingProfileTest
{
	private static readonly AppConfig Global = new()
	{
		RecordDirectory = "records",
		RecorderType = RecorderType.FFmpeg,
		CodecPriority = [StreamCodec.Hevc, StreamCodec.Avc],
		FormatPriority = [StreamFormat.Flv],
		StreamHostSelection = StreamHostSelection.Random,
		ConvertToMp4 = true,
		DeleteAfterConvert = true
	};

	[Test]
	public async Task RoomInheritsGlobalSettingsWhenNotOverriddenAsync()
	{
		RecordingProfile profile = RecordingProfile.Create(Global, new RoomConfig { RoomId = 1, Quality = StreamQuality.BluRay });

		await Assert.That(profile.RecordDirectory).IsEqualTo("records");
		await Assert.That(profile.RecorderType).IsEqualTo(RecorderType.FFmpeg);
		await Assert.That(profile.Quality).IsEqualTo(StreamQuality.BluRay);
		await Assert.That(profile.CodecPriority.SequenceEqual([StreamCodec.Hevc, StreamCodec.Avc])).IsTrue();
		await Assert.That(profile.FormatPriority.SequenceEqual([StreamFormat.Flv])).IsTrue();
		await Assert.That(profile.HostSelection).IsEqualTo(StreamHostSelection.Random);
		await Assert.That(profile.ConvertToMp4).IsTrue();
		await Assert.That(profile.DeleteAfterConvert).IsTrue();
	}

	[Test]
	public async Task RoomOverridesWinOverGlobalSettingsAsync()
	{
		RoomConfig room = new()
		{
			RoomId = 1,
			RecorderType = RecorderType.BuiltIn,
			CodecPriority = [StreamCodec.Av1],
			FormatPriority = [StreamFormat.Ts, StreamFormat.Fmp4],
			ConvertToMp4 = false,
			DeleteAfterConvert = false,
			StreamIdleTimeout = TimeSpan.FromSeconds(30)
		};

		RecordingProfile profile = RecordingProfile.Create(Global, room);

		await Assert.That(profile.RecorderType).IsEqualTo(RecorderType.BuiltIn);
		await Assert.That(profile.CodecPriority.SequenceEqual([StreamCodec.Av1])).IsTrue();
		await Assert.That(profile.FormatPriority.SequenceEqual([StreamFormat.Ts, StreamFormat.Fmp4])).IsTrue();
		await Assert.That(profile.ConvertToMp4).IsFalse();
		await Assert.That(profile.DeleteAfterConvert).IsFalse();
		await Assert.That(profile.StreamIdleTimeout).IsEqualTo(TimeSpan.FromSeconds(30));
	}
}
