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
			RecorderType = (RecorderType)99,
			StreamHostSelection = (StreamHostSelection)99,
			SplitMode = (RecordingSplitMode)99,
			GiftMode = (DanmakuGiftMode)99
		};

		config.ApplyDefaults();

		await Assert.That((config.Theme, config.RoomSortOrder, config.RecorderType, config.StreamHostSelection, config.SplitMode, config.GiftMode)).IsEqualTo((AppTheme.FollowSystem, RoomSortOrder.Status, RecorderType.BuiltIn, StreamHostSelection.FirstResponse, RecordingSplitMode.None, DanmakuGiftMode.None));
	}

	[Test]
	[Arguments((RecorderType)99, (RecordingSplitMode)99, null, null)]
	[Arguments(RecorderType.FFmpeg, RecordingSplitMode.None, RecorderType.FFmpeg, RecordingSplitMode.None)]
	public async Task RoomEnumOverridesInheritTheAppSettingsOnlyWhenUndefinedAsync(RecorderType recorderType, RecordingSplitMode splitMode, RecorderType? expectedRecorderType, RecordingSplitMode? expectedSplitMode)
	{
		RoomConfig room = new() { RoomId = 1, RecorderType = recorderType, SplitMode = splitMode };

		room.ApplyDefaults();

		await Assert.That((room.RecorderType, room.SplitMode)).IsEqualTo((expectedRecorderType, expectedSplitMode));
	}

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
		RoomConfig room = new()
		{
			RoomId = 1,
			StatusCheckInterval = huge,
			DanmuReconnectDelay = huge,
			StreamReconnectDelay = huge,
			StreamConnectTimeout = huge,
			StreamIdleTimeout = TimeSpan.MaxValue
		};

		room.ApplyDefaults();

		TimeSpan max = TimeSpan.FromSeconds((double)RoomDefaults.MaxIntervalSeconds);
		await Assert.That([room.StatusCheckInterval, room.DanmuReconnectDelay, room.StreamReconnectDelay, room.StreamConnectTimeout, room.StreamIdleTimeout]).All(value => value == max);
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

	// 输入框和手改的配置里多出的空白不算数，只有空白时等于没填；读的地方只判断 null
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
			SplitSizeMegabytes = 1,
			Rooms = [new RoomConfig { RoomId = 1, SplitDuration = TimeSpan.FromDays(3), SplitSizeMegabytes = long.MaxValue }]
		};

		config.ApplyDefaults();

		using (Assert.Multiple())
		{
			await Assert.That(config.SplitDuration).IsEqualTo(TimeSpan.FromMinutes(1));
			await Assert.That(config.SplitSizeMegabytes).IsEqualTo(64);
			await Assert.That(config.Rooms[0].SplitDuration).IsEqualTo(TimeSpan.FromHours(24));
			await Assert.That(config.Rooms[0].SplitSizeMegabytes).IsEqualTo(1 << 20);
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
}
