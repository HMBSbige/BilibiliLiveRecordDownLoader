namespace BilibiliLiveRecordDownLoader.Application.Tests.Domain;

public sealed class ConfigDefaultsTest
{
	[Test]
	public async Task UndefinedEnumValuesFallBackToTheirDefaultsAsync()
	{
		AppConfig config = new()
		{
			Theme = (AppTheme)99,
			RoomSortOrder = (RoomSortOrder)99,
			StreamHostSelection = (StreamHostSelection)99,
			SplitMode = (RecordingSplitMode)99,
			GiftMode = (DanmakuGiftMode)99
		};

		config.ApplyDefaults();

		await Assert.That((config.Theme, config.RoomSortOrder, config.StreamHostSelection, config.SplitMode, config.GiftMode)).IsEqualTo((AppTheme.FollowSystem, RoomSortOrder.Status, StreamHostSelection.FirstResponse, RecordingSplitMode.None, DanmakuGiftMode.None));
	}

	[Test]
	[Arguments((RecordingSplitMode)99, null)]
	[Arguments(RecordingSplitMode.None, RecordingSplitMode.None)]
	public async Task RoomEnumOverridesInheritTheAppSettingsOnlyWhenUndefinedAsync(RecordingSplitMode splitMode, RecordingSplitMode? expected)
	{
		RoomConfig room = new RoomConfig { RoomId = 1, SplitMode = splitMode }.Normalize();

		await Assert.That(room.SplitMode).IsEqualTo(expected);
	}

	[Test]
	public async Task RoomDefaultsClampTimingsToLowerBoundsAsync()
	{
		RoomConfig room = new RoomConfig
		{
			RoomId = 1,
			StatusCheckInterval = TimeSpan.FromSeconds(1),
			DanmuReconnectDelay = TimeSpan.Zero,
			StreamReconnectDelay = TimeSpan.FromSeconds(-5),
			StreamConnectTimeout = TimeSpan.FromMilliseconds(10),
			StreamIdleTimeout = TimeSpan.FromSeconds(1)
		}.Normalize();

		using (Assert.Multiple())
		{
			await Assert.That(room.StatusCheckInterval).IsEqualTo(TimeSpan.FromSeconds(10));
			await Assert.That(room.DanmuReconnectDelay).IsEqualTo(TimeSpan.FromSeconds(1));
			await Assert.That(room.StreamReconnectDelay).IsEqualTo(TimeSpan.FromSeconds(1));
			await Assert.That(room.StreamConnectTimeout).IsEqualTo(TimeSpan.FromSeconds(1));
			await Assert.That(room.StreamIdleTimeout).IsEqualTo(TimeSpan.FromSeconds(3));
		}
	}

	[Test]
	public async Task RoomDefaultsClampTimingsToTheUpperBoundAsync()
	{
		TimeSpan huge = TimeSpan.FromDays(100);
		RoomConfig room = new RoomConfig
		{
			RoomId = 1,
			StatusCheckInterval = huge,
			DanmuReconnectDelay = huge,
			StreamReconnectDelay = huge,
			StreamConnectTimeout = huge,
			StreamIdleTimeout = TimeSpan.MaxValue
		}.Normalize();

		await Assert.That([room.StatusCheckInterval, room.DanmuReconnectDelay, room.StreamReconnectDelay, room.StreamConnectTimeout, room.StreamIdleTimeout]).All(static value => value == RoomDefaults.MaxInterval);
	}

	[Test]
	public async Task RoomDefaultsTurnUnusablePriorityOverridesIntoInheritanceAsync()
	{
		RoomConfig room = new RoomConfig
		{
			RoomId = 1,
			CodecPriority = [],
			FormatPriority = [(StreamFormat)42]
		}.Normalize();

		using (Assert.Multiple())
		{
			await Assert.That(room.CodecPriority).IsNull();
			await Assert.That(room.FormatPriority).IsNull();
		}
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

		using (Assert.Multiple())
		{
			await Assert.That(config.RecordDirectory).IsEqualTo(ConfigurationConsts.DefaultRecordDirectory);
			await Assert.That(config.CodecPriority).IsEquivalentTo([StreamCodec.Hevc], CollectionOrdering.Matching);
			await Assert.That(config.FormatPriority).IsEquivalentTo(StreamPriority.DefaultFormats, CollectionOrdering.Matching);
			await Assert.That(config.Rooms.Select(static room => (room.RoomId, room.Quality))).IsEquivalentTo([(2L, StreamQuality.High), (3L, RoomDefaults.Quality)], CollectionOrdering.Matching);
		}
	}

	[Test]
	public async Task AppDefaultsNormalizeCopiesOfTheRoomsAsync()
	{
		RoomConfig room = new() { RoomId = 1, StatusCheckInterval = TimeSpan.FromSeconds(1) };
		AppConfig config = new() { Rooms = [room] };

		config.ApplyDefaults();

		await Assert.That((room.StatusCheckInterval, config.Rooms[0].StatusCheckInterval)).IsEqualTo((TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10)));
	}

	[Test]
	public async Task TextSettingsAreTrimmedAndBlankOnesClearedAsync()
	{
		AppConfig padded = new() { Language = " en ", RecordDirectory = " records ", FFmpegPath = " ffmpeg ", UserAgent = " ua ", Cookie = " SESSDATA=x " };
		AppConfig blank = new() { Language = "", FFmpegPath = " ", UserAgent = "\t", Cookie = "  " };

		padded.ApplyDefaults();
		blank.ApplyDefaults();

		using (Assert.Multiple())
		{
			await Assert.That((padded.Language, padded.RecordDirectory, padded.FFmpegPath, padded.UserAgent, padded.Cookie)).IsEqualTo(("en", "records", "ffmpeg", "ua", "SESSDATA=x"));
			await Assert.That([blank.Language, blank.FFmpegPath, blank.UserAgent, blank.Cookie]).All(static value => value is null);
		}
	}

	[Test]
	public async Task SplitValuesAreClampedToTheirBoundsAsync()
	{
		AppConfig config = new()
		{
			SplitDuration = TimeSpan.FromSeconds(10),
			SplitSizeMebibytes = 1,
			Rooms = [new RoomConfig { RoomId = 1, SplitDuration = TimeSpan.FromDays(3), SplitSizeMebibytes = long.MaxValue }]
		};

		config.ApplyDefaults();

		using (Assert.Multiple())
		{
			await Assert.That(config.SplitDuration).IsEqualTo(TimeSpan.FromMinutes(1));
			await Assert.That(config.SplitSizeMebibytes).IsEqualTo(64);
			await Assert.That(config.Rooms[0].SplitDuration).IsEqualTo(TimeSpan.FromHours(24));
			await Assert.That(config.Rooms[0].SplitSizeMebibytes).IsEqualTo(1 << 20);
		}
	}

	[Test]
	public async Task DanmakuOffsetsAreClampedToAMinuteEitherWayAsync()
	{
		AppConfig config = new()
		{
			DanmakuOffset = TimeSpan.FromMinutes(-5),
			Rooms = [new RoomConfig { RoomId = 1, DanmakuOffset = TimeSpan.FromSeconds(90) }, new RoomConfig { RoomId = 2, DanmakuOffset = TimeSpan.FromSeconds(-1.5) }]
		};

		config.ApplyDefaults();

		using (Assert.Multiple())
		{
			await Assert.That(config.DanmakuOffset).IsEqualTo(TimeSpan.FromSeconds(-60));
			await Assert.That(config.Rooms[0].DanmakuOffset).IsEqualTo(TimeSpan.FromSeconds(60));
			await Assert.That(config.Rooms[1].DanmakuOffset).IsEqualTo(TimeSpan.FromSeconds(-1.5));
		}
	}

	[Test]
	public async Task DanmakuOffsetsAreRoundedToWholeMillisecondsAsync()
	{
		AppConfig config = new()
		{
			DanmakuOffset = TimeSpan.FromTicks(536_689_999),
			Rooms = [new RoomConfig { RoomId = 1, DanmakuOffset = TimeSpan.FromTicks(-536_689_999) }]
		};

		config.ApplyDefaults();

		await Assert.That((config.DanmakuOffset, config.Rooms[0].DanmakuOffset)).IsEqualTo((TimeSpan.FromMilliseconds(53_669), TimeSpan.FromMilliseconds(-53_669)));
	}
}
