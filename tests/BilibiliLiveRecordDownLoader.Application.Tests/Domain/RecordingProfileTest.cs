using TUnit.Assertions.Enums;

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

		using (Assert.Multiple())
		{
			await Assert.That(profile.RecordDirectory).IsEqualTo("records");
			await Assert.That(profile.RecorderType).IsEqualTo(RecorderType.FFmpeg);
			await Assert.That(profile.Quality).IsEqualTo(StreamQuality.BluRay);
			await Assert.That(profile.CodecPriority).IsEquivalentTo([StreamCodec.Hevc, StreamCodec.Avc], CollectionOrdering.Matching);
			await Assert.That(profile.FormatPriority).IsEquivalentTo([StreamFormat.Flv], CollectionOrdering.Matching);
			await Assert.That(profile.HostSelection).IsEqualTo(StreamHostSelection.Random);
			await Assert.That((profile.ConvertToMp4, profile.DeleteAfterConvert)).IsEqualTo((true, true));
		}
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

		using (Assert.Multiple())
		{
			await Assert.That(profile.RecorderType).IsEqualTo(RecorderType.BuiltIn);
			await Assert.That(profile.CodecPriority).IsEquivalentTo([StreamCodec.Av1], CollectionOrdering.Matching);
			await Assert.That(profile.FormatPriority).IsEquivalentTo([StreamFormat.Ts, StreamFormat.Fmp4], CollectionOrdering.Matching);
			await Assert.That((profile.ConvertToMp4, profile.DeleteAfterConvert)).IsEqualTo((false, false));
			await Assert.That(profile.StreamIdleTimeout).IsEqualTo(TimeSpan.FromSeconds(30));
		}
	}

	[Test]
	[Arguments(StreamQuality.Original, true, 25000L)]
	[Arguments(StreamQuality.Original, false, 10000L)]
	[Arguments(StreamQuality.UltraHd4K, true, 20000L)]
	[Arguments(StreamQuality.BluRay, true, 400L)]
	public async Task TrueOriginalIsRequestedOnlyForTheOriginalQualityAsync(StreamQuality roomQuality, bool requestTrueOriginal, long expected)
	{
		AppConfig app = Global with { RequestTrueOriginal = requestTrueOriginal };

		RecordingProfile profile = RecordingProfile.Create(app, new RoomConfig { RoomId = 1, Quality = roomQuality });

		await Assert.That(profile.RequestQuality).IsEqualTo(expected);
	}
}
