namespace BilibiliLiveRecordDownLoader.Application.Tests.Domain;

public sealed class ConfigDefaultsTest
{
	[Test]
	public async Task RoomDefaultsClampTimingsToLowerBoundsAsync()
	{
		RoomConfig room = new()
		{
			RoomId = 1,
			StatusCheckInterval = TimeSpan.FromSeconds(1),
			DanmuReconnectDelay = TimeSpan.Zero,
			StreamReconnectDelay = TimeSpan.FromSeconds(-5),
			StreamConnectTimeout = TimeSpan.FromMilliseconds(10),
			StreamIdleTimeout = TimeSpan.FromSeconds(1)
		};

		room.ApplyDefaults();

		await Assert.That(room.StatusCheckInterval).IsEqualTo(TimeSpan.FromSeconds(10));
		await Assert.That(room.DanmuReconnectDelay).IsEqualTo(TimeSpan.FromSeconds(1));
		await Assert.That(room.StreamReconnectDelay).IsEqualTo(TimeSpan.FromSeconds(1));
		await Assert.That(room.StreamConnectTimeout).IsEqualTo(TimeSpan.FromSeconds(1));
		await Assert.That(room.StreamIdleTimeout).IsEqualTo(TimeSpan.FromSeconds(3));
	}

	[Test]
	public async Task RoomDefaultsTurnUnusablePriorityOverridesIntoInheritanceAsync()
	{
		RoomConfig room = new()
		{
			RoomId = 1,
			CodecPriority = [],
			FormatPriority = [(StreamFormat)42]
		};

		room.ApplyDefaults();

		await Assert.That(room.CodecPriority).IsNull();
		await Assert.That(room.FormatPriority).IsNull();
	}

	[Test]
	public async Task AppDefaultsFillMissingValuesAndDropInvalidRoomsAsync()
	{
		AppConfig config = new()
		{
			RecordDirectory = "  ",
			CodecPriority = [StreamCodec.Hevc, StreamCodec.Hevc],
			Rooms =
			[
				new RoomConfig { RoomId = 0 },
				new RoomConfig { RoomId = 2, Quality = StreamQuality.High },
				new RoomConfig { RoomId = 2, Quality = StreamQuality.Smooth },
				new RoomConfig { RoomId = 3 }
			]
		};

		config.ApplyDefaults();

		await Assert.That(config.RecordDirectory).IsEqualTo(ConfigurationConsts.DefaultRecordDirectory);
		await Assert.That(config.CodecPriority.SequenceEqual([StreamCodec.Hevc])).IsTrue();
		await Assert.That(config.FormatPriority.SequenceEqual(StreamPriority.DefaultFormats)).IsTrue();
		await Assert.That(config.Rooms.Select(static room => room.RoomId).SequenceEqual([2L, 3L])).IsTrue();
		await Assert.That(config.Rooms[0].Quality).IsEqualTo(StreamQuality.High);
	}
}
