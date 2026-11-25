using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using Microsoft.Extensions.Logging;
using System.Runtime.Versioning;
using OS = TUnit.Core.Enums.OS;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Configuration;

public sealed class AppConfigManagerTest : IDisposable
{
	private const string ConfigFileName = "config.json";
	private const string LegacyFileName = "BilibiliLiveRecordDownLoader.json";

	private readonly TestDirectory _directory = new();

	private string ConfigPath => _directory.Combine(ConfigFileName);

	public void Dispose()
	{
		_directory.Dispose();
	}

	[Test]
	public async Task UpdatesArePersistedReadablyWithoutDefaultValuesAsync(CancellationToken cancellationToken)
	{
		await using (AppConfigManager manager = Create())
		{
			await manager.UpdateAsync(static config =>
			{
				config.RecordDirectory = "录播";
				config.Theme = AppTheme.Dark;
				config.Rooms = [.. config.Rooms, new RoomConfig { RoomId = 732, SplitMode = RecordingSplitMode.Size }];
			}, cancellationToken);
		}

		string text = await File.ReadAllTextAsync(ConfigPath, cancellationToken);
		JsonObject saved = await ReadSavedAsync(cancellationToken);
		AppConfig config = await ReloadAsync(cancellationToken);
		RoomConfig room = await Assert.That(config.Rooms).HasSingleItem();

		using (Assert.Multiple())
		{
			await Assert.That(saved.Select(static property => property.Key)).IsEquivalentTo([nameof(AppConfig.Theme), nameof(AppConfig.RecordDirectory), nameof(AppConfig.Rooms)], CollectionOrdering.Matching);
			await Assert.That(SavedRoom(saved).Select(static property => property.Key)).IsEquivalentTo([nameof(RoomConfig.RoomId), nameof(RoomConfig.SplitMode)], CollectionOrdering.Matching);
			await Assert.That(saved[nameof(AppConfig.Theme)]?.GetValue<string>()).IsEqualTo(nameof(AppTheme.Dark));
			await Assert.That(SavedRoom(saved)[nameof(RoomConfig.SplitMode)]?.GetValue<string>()).IsEqualTo(nameof(RecordingSplitMode.Size));
			await Assert.That(text).Contains("\"录播\"");
			await Assert.That((config.RecordDirectory, config.Theme)).IsEqualTo(("录播", AppTheme.Dark));
			await Assert.That(room).IsEqualTo(new RoomConfig { RoomId = 732, SplitMode = RecordingSplitMode.Size });
		}
	}

	[Test]
	public async Task ConfigThatCannotBeReadIsNeverOverwrittenAsync(CancellationToken cancellationToken)
	{
		const string content = """{ "Rooms": [{ "RoomId": 1 }] }""";
		await WriteFileAsync(ConfigFileName, content, cancellationToken);
		AppConfig loaded;

		await using (AppConfigManager manager = Create())
		{
			await using (new FileStream(ConfigPath, FileMode.Open, FileAccess.Read, FileShare.None))
			{
				loaded = await manager.GetAsync(cancellationToken);
			}

			await manager.UpdateAsync(static config => config.Theme = AppTheme.Dark, cancellationToken);
		}

		using (Assert.Multiple())
		{
			await Assert.That(loaded.Rooms).IsEmpty();
			await Assert.That(await File.ReadAllTextAsync(ConfigPath, cancellationToken)).IsEqualTo(content);
			await Assert.That(File.Exists(ConfigPath + ".bak")).IsFalse();
		}
	}

	[Test]
	public async Task PendingChangesAreFlushedOnDisposeAsync(CancellationToken cancellationToken)
	{
		AppConfigManager manager = Create(TimeSpan.FromMinutes(1), time: new FakeTimeProvider());
		await manager.UpdateAsync(static config => config.Language = "zh-Hans", cancellationToken);
		await Assert.That(File.Exists(ConfigPath)).IsFalse();

		// 测的就是同步 Dispose
#pragma warning disable CA1849
		manager.Dispose();
#pragma warning restore CA1849

		await Assert.That((await ReloadAsync(cancellationToken)).Language).IsEqualTo("zh-Hans");
	}

	[Test]
	public async Task ConfigLoadedAfterDisposeIsNotWrittenAsync(CancellationToken cancellationToken)
	{
		await WriteFileAsync(LegacyFileName, """{ "Rooms": [{ "RoomId": 732 }] }""", cancellationToken);
		AppConfigManager manager = Create();
		await manager.DisposeAsync();

		AppConfig config = await manager.GetAsync(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(RoomIds(config)).IsEquivalentTo((long[])[732], CollectionOrdering.Matching);
			await Assert.That(manager.IsLegacyConfigImported).IsTrue();
			await Assert.That(File.Exists(ConfigPath)).IsFalse();
		}
	}

	[Test]
	public async Task CallerCancellationDoesNotDropTheDebouncedSaveAsync(CancellationToken cancellationToken)
	{
		TimerSignalingTimeProvider time = new();
		await using AppConfigManager manager = Create(TimeSpan.FromSeconds(1), time: time);

		using (CancellationTokenSource caller = new())
		{
			await manager.UpdateAsync(static config => config.Language = "zh-Hans", caller.Token);
			await caller.CancelAsync();
		}

		await time.TimerCreated.Task.WaitAsync(TestWait.DefaultTimeout, cancellationToken);
		time.Advance(TimeSpan.FromSeconds(1));
		await TestWait.UntilAsync(() => File.Exists(ConfigPath), "the update is saved although its caller gave up", cancellationToken);

		await Assert.That((await ReloadAsync(cancellationToken)).Language).IsEqualTo("zh-Hans");
	}

	[Test]
	public async Task FailedSaveIsRetriedWithoutAnotherUpdateAsync(CancellationToken cancellationToken)
	{
		TimerSignalingTimeProvider time = new();
		Directory.CreateDirectory(ConfigPath + ".tmp");
		await using AppConfigManager manager = Create(time: time);

		await manager.UpdateAsync(static config => config.Language = "zh-Hans", cancellationToken);
		await time.TimerCreated.Task.WaitAsync(TestWait.DefaultTimeout, cancellationToken);
		Directory.Delete(ConfigPath + ".tmp");
		time.Advance(TimeSpan.FromMinutes(1));

		await TestWait.UntilAsync(() => File.Exists(ConfigPath), "the failed save is retried", cancellationToken);
		await Assert.That((await ReloadAsync(cancellationToken)).Language).IsEqualTo("zh-Hans");
	}

	[Test]
	public async Task UpdateAndSaveWritesBeforeReturningAndThrowsWhenItCannotAsync(CancellationToken cancellationToken)
	{
		await using AppConfigManager manager = Create(TimeSpan.FromMinutes(1), time: new FakeTimeProvider());

		await manager.UpdateAndSaveAsync(static config => config.Language = "zh-Hans", cancellationToken);
		await Assert.That((await ReadSavedAsync(cancellationToken))[nameof(AppConfig.Language)]?.GetValue<string>()).IsEqualTo("zh-Hans");
		Directory.CreateDirectory(ConfigPath + ".tmp");

		Exception? exception = await Assert.ThrowsAsync<Exception>(async () => await manager.UpdateAndSaveAsync(static config => config.Language = "en", cancellationToken));

		using (Assert.Multiple())
		{
			// 用目录占住临时文件时，各平台抛出的异常类型不同
			await Assert.That(exception is IOException or UnauthorizedAccessException).IsTrue();
			await Assert.That((await ReadSavedAsync(cancellationToken))[nameof(AppConfig.Language)]?.GetValue<string>()).IsEqualTo("zh-Hans");
		}
	}

	[Test]
	[Arguments("""{ "Theme": "Dark", "Rooms": null }""", new long[] { })]
	[Arguments("""{ "Theme": "Dark", "Rooms": [null, { "RoomId": 1 }] }""", new long[] { 1 })]
	public async Task NullRoomsAreSkippedAsync(string json, long[] expectedRooms, CancellationToken cancellationToken)
	{
		await WriteFileAsync(ConfigFileName, json, cancellationToken);

		await using AppConfigManager manager = Create();
		AppConfig config = await manager.GetAsync(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(config.Theme).IsEqualTo(AppTheme.Dark);
			await Assert.That(RoomIds(config)).IsEquivalentTo(expectedRooms, CollectionOrdering.Matching);
		}
	}

	[Test]
	public async Task UnreadableValuesFallBackToTheirDefaultsWithoutDiscardingTheRestAsync(CancellationToken cancellationToken)
	{
		await WriteFileAsync
		(
			ConfigFileName,
			"""
			{
				// 手改的配置
				"theme": "Future",
				"Cookie": "a=1",
				"CodecPriority": ["Hevc", "Future", "Avc"],
				"FormatPriority": ["Future"],
				"SplitDuration": 42,
				"Rooms": [{ "RoomId": 1, "Quality": "Future", "IsNotificationEnabled": true, "FormatPriority": ["Ts", "Future", null] }, 5, { "RoomId": 2 }],
			}
			""",
			cancellationToken
		);

		await using (AppConfigManager manager = Create())
		{
			AppConfig config = await manager.GetAsync(cancellationToken);

			using (Assert.Multiple())
			{
				await Assert.That(config.Theme).IsEqualTo(AppConfig.CreateDefault().Theme);
				await Assert.That(config.Cookie).IsEqualTo("a=1");
				await Assert.That(config.CodecPriority).IsEquivalentTo([StreamCodec.Hevc, StreamCodec.Avc], CollectionOrdering.Matching);
				await Assert.That(config.FormatPriority).IsEquivalentTo(StreamPriority.DefaultFormats, CollectionOrdering.Matching);
				await Assert.That(config.SplitDuration).IsEqualTo(RecordingSplitDefaults.Duration);
				await Assert.That(RoomIds(config)).IsEquivalentTo([1L, 2L], CollectionOrdering.Matching);
				await Assert.That((config.Rooms[0].Quality, config.Rooms[0].IsNotificationEnabled)).IsEqualTo((RoomDefaults.Quality, true));
				await Assert.That(config.Rooms[0].FormatPriority).IsEquivalentTo([StreamFormat.Ts], CollectionOrdering.Matching);
				await Assert.That(CorruptedFiles()).IsEmpty();
			}
		}

		JsonObject saved = await ReadSavedAsync(cancellationToken);
		await Assert.That(saved.Select(static property => property.Key)).DoesNotContain("theme").And.DoesNotContain(nameof(AppConfig.Theme));
	}

	[Test]
	public async Task UpdatesAreClampedToTheirBoundsAsync(CancellationToken cancellationToken)
	{
		await using AppConfigManager manager = Create();

		await manager.UpdateAsync(static config =>
		{
			config.DanmakuOffset = TimeSpan.FromMinutes(10);
			config.SplitDuration = TimeSpan.Zero;
		}, cancellationToken);
		AppConfig config = await manager.GetAsync(cancellationToken);

		await Assert.That((config.DanmakuOffset, config.SplitDuration)).IsEqualTo((DanmakuDefaults.MaxOffset, RecordingSplitDefaults.MinDuration));
	}

	[Test]
	[Arguments("{ not json")]
	[Arguments("""{ "Cookie": "a=1", "Cookie": "b=2", "Theme": "Future" }""")]
	public async Task CorruptedFileFallsBackToDefaultsAsync(string content, CancellationToken cancellationToken)
	{
		await WriteFileAsync(ConfigFileName, content, cancellationToken);

		await using AppConfigManager manager = Create();
		AppConfig config = await manager.GetAsync(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(config).IsEquivalentTo(AppConfig.CreateDefault());
			await Assert.That(CorruptedFiles()).Count().IsEqualTo(1);
		}
	}

	[Test]
	[Arguments("{ truncated")]
	[Arguments("null")]
	public async Task CorruptedFileIsRestoredFromBackupAsync(string content, CancellationToken cancellationToken)
	{
		await using (AppConfigManager manager = Create())
		{
			await manager.UpdateAsync(static config => config.Rooms = [.. config.Rooms, new RoomConfig { RoomId = 732 }], cancellationToken);
		}

		await using (AppConfigManager manager = Create())
		{
			await manager.UpdateAsync(static config => config.Theme = AppTheme.Dark, cancellationToken);
		}

		await WriteFileAsync(ConfigFileName, content, cancellationToken);

		await using (AppConfigManager manager = Create())
		{
			await Assert.That(RoomIds(await manager.GetAsync(cancellationToken))).IsEquivalentTo([732L], CollectionOrdering.Matching);
		}

		AppConfig reloaded = await ReloadAsync(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(RoomIds(reloaded)).IsEquivalentTo([732L], CollectionOrdering.Matching);
			await Assert.That(CorruptedFiles()).Count().IsEqualTo(1);
		}
	}

	[Test]
	public async Task CorruptedFileIsStillRestoredAfterTheRestoreWasCanceledAsync(CancellationToken cancellationToken)
	{
		await WriteFileAsync(ConfigFileName, "{ truncated", cancellationToken);
		await WriteFileAsync(ConfigFileName + ".bak", """{ "Rooms": [{ "RoomId": 1 }] }""", cancellationToken);

		using (CancellationTokenSource caller = new())
		{
			await using AppConfigManager manager = Create(logger: new CancelOnErrorLogger(caller));

			await Assert.ThrowsAsync<OperationCanceledException>(async () => await manager.GetAsync(caller.Token));
		}

		await Assert.That(RoomIds(await ReloadAsync(cancellationToken))).IsEquivalentTo([1L], CollectionOrdering.Matching);
	}

	[Test]
	public async Task CorruptedFileIsStillRestoredAfterSavingTheRestoredConfigFailedAsync(CancellationToken cancellationToken)
	{
		const string backup = """{ "Rooms": [{ "RoomId": 1 }] }""";
		await WriteFileAsync(ConfigFileName, "{ truncated", cancellationToken);
		await WriteFileAsync(ConfigFileName + ".bak", backup, cancellationToken);

		Directory.CreateDirectory(ConfigPath + ".tmp");
		AppConfig restored = await ReloadAsync(cancellationToken);
		Directory.Delete(ConfigPath + ".tmp");
		AppConfig reloaded = await ReloadAsync(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(RoomIds(restored)).IsEquivalentTo([1L], CollectionOrdering.Matching);
			await Assert.That(RoomIds(reloaded)).IsEquivalentTo([1L], CollectionOrdering.Matching);
			await Assert.That(await File.ReadAllTextAsync(ConfigPath + ".bak", cancellationToken)).IsEqualTo(backup).Because("写回时损坏的文件不能替换掉备份");
		}
	}

	[Test]
	[Arguments(true)]
	[Arguments(false)]
	[ExcludeOn(OS.Windows)]
	[UnsupportedOSPlatform("windows")]
	public async Task CorruptedFileIsOnlyReadableByTheOwnerAsync(bool hasBackup, CancellationToken cancellationToken)
	{
		const UnixFileMode ownerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;
		await WriteFileAsync(ConfigFileName, """{ "Cookie": "SESSDATA=1", truncated""", cancellationToken);
		File.SetUnixFileMode(ConfigPath, ownerOnly | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

		if (hasBackup)
		{
			await WriteFileAsync(ConfigFileName + ".bak", "{}", cancellationToken);
		}

		await ReloadAsync(cancellationToken);
		string corrupted = await Assert.That(CorruptedFiles()).HasSingleItem();

		await Assert.That(File.GetUnixFileMode(corrupted)).IsEqualTo(ownerOnly);
	}

	[Test]
	public async Task InterruptedSaveIsRestoredByMovingTheTempFileBackAsync(CancellationToken cancellationToken)
	{
		const string temp = """{ "Rooms": [{ "RoomId": 732 }] }""";
		const string backup = """{ "Rooms": [{ "RoomId": 1 }] }""";
		await WriteFileAsync(ConfigFileName + ".tmp", temp, cancellationToken);
		await WriteFileAsync(ConfigFileName + ".bak", backup, cancellationToken);

		AppConfig config = await ReloadAsync(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(RoomIds(config)).IsEquivalentTo([732L], CollectionOrdering.Matching);
			await Assert.That(await File.ReadAllTextAsync(ConfigPath, cancellationToken)).IsEqualTo(temp);
			await Assert.That(await File.ReadAllTextAsync(ConfigPath + ".bak", cancellationToken)).IsEqualTo(backup);
			await Assert.That(File.Exists(ConfigPath + ".tmp")).IsFalse();
		}
	}

	[Test]
	public async Task InterruptedSaveWithAnUnreadableTempFileIsRestoredFromTheBackupAsync(CancellationToken cancellationToken)
	{
		await WriteFileAsync(ConfigFileName + ".tmp", "{ truncated", cancellationToken);
		await WriteFileAsync(ConfigFileName + ".bak", """{ "Rooms": [{ "RoomId": 1 }] }""", cancellationToken);

		AppConfig config = await ReloadAsync(cancellationToken);
		JsonObject saved = await ReadSavedAsync(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(RoomIds(config)).IsEquivalentTo([1L], CollectionOrdering.Matching);
			await Assert.That(SavedRoom(saved)[nameof(RoomConfig.RoomId)]?.GetValue<long>()).IsEqualTo(1);
			await Assert.That(File.Exists(ConfigPath + ".tmp")).IsFalse();
		}
	}

	[Test]
	public async Task InterruptedSaveWithALockedTempFileIsNeverOverwrittenAsync(CancellationToken cancellationToken)
	{
		const string temp = """{ "Rooms": [{ "RoomId": 732 }] }""";
		const string backup = """{ "Rooms": [{ "RoomId": 1 }] }""";
		await WriteFileAsync(ConfigFileName + ".tmp", temp, cancellationToken);
		await WriteFileAsync(ConfigFileName + ".bak", backup, cancellationToken);
		AppConfig loaded;

		await using (AppConfigManager manager = Create())
		{
			await using (new FileStream(ConfigPath + ".tmp", FileMode.Open, FileAccess.Read, FileShare.None))
			{
				loaded = await manager.GetAsync(cancellationToken);
			}

			await manager.UpdateAsync(static config => config.Theme = AppTheme.Dark, cancellationToken);
		}

		using (Assert.Multiple())
		{
			await Assert.That(loaded.Rooms).IsEmpty();
			await Assert.That(await File.ReadAllTextAsync(ConfigPath + ".tmp", cancellationToken)).IsEqualTo(temp);
			await Assert.That(await File.ReadAllTextAsync(ConfigPath + ".bak", cancellationToken)).IsEqualTo(backup);
			await Assert.That(File.Exists(ConfigPath)).IsFalse();
		}
	}

	[Test]
	public async Task DeletedConfigIsNotRestoredFromTheBackupAsync(CancellationToken cancellationToken)
	{
		await WriteFileAsync(ConfigFileName + ".bak", """{ "Rooms": [{ "RoomId": 1 }] }""", cancellationToken);

		await using AppConfigManager manager = Create();
		AppConfig config = await manager.GetAsync(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(config.Rooms).IsEmpty();
			await Assert.That(File.Exists(ConfigPath)).IsFalse();
		}
	}

	[Test]
	public async Task LegacyConfigIsImportedAndSavedWithoutTouchingTheOldFileAsync(CancellationToken cancellationToken)
	{
		const string legacy = """
			{
			  "MainDir": "D:\\Records",
			  "IsCheckUpdateOnStart": false,
			  "IsCheckPreRelease": true,
			  "MainWindowsWidth": 1600.0,
			  "MainWindowsHeight": 900.0,
			  "Cookie": "SESSDATA=x",
			  "Rooms": [
			    {
			      "RoomId": 732,
			      "IsNotify": true,
			      "HttpCheckLatency": 60.0,
			      "StreamTimeout": 30.5,
			      "ClientType": 1,
			      "Qn": 400,
			      "RecorderType": 3,
			      "IsAutoConvertMp4": true,
			      "AutoRecordCodecOrder": "hevc;unknown;avc"
			    },
			    {
			      "IsEnable": false,
			      "RoomId": 6,
			      "Qn": 12345,
			      "RecorderType": 1,
			      "AutoRecordFormatOrder": "unknown"
			    },
			    {
			      "RoomId": 7,
			      "RecorderType": 0,
			      "StreamReconnectLatency": "6"
			    }
			  ],
			  "IsAutoConvertMp4": true,
			  "IsDeleteAfterConvert": true,
			  "IsUseProxy": true,
			  "Theme": 2,
			  "RecorderType": 3,
			  "AutoRecordFormatOrder": " flv ; ts ",
			  "StreamHostRule": 2
			}
			""";
		await WriteFileAsync(LegacyFileName, legacy, cancellationToken);

		await using (AppConfigManager manager = Create())
		{
			AppConfig config = await manager.GetAsync(cancellationToken);
			RoomConfig room = config.Rooms[0];
			RoomConfig disabled = config.Rooms[1];
			RoomConfig inheriting = config.Rooms[2];

			using (Assert.Multiple())
			{
				await Assert.That(manager.IsLegacyConfigImported).IsTrue();
				await Assert.That(config.RecordDirectory).IsEqualTo(@"D:\Records");
				await Assert.That((config.AutoCheckUpdate, config.IncludePreRelease, config.UseSystemProxy, config.ConvertToMp4, config.DeleteAfterConvert)).IsEqualTo((false, true, true, true, true));
				await Assert.That((config.WindowWidth, config.WindowHeight)).IsEqualTo((1600d, 900d));
				await Assert.That(config.Cookie).IsEqualTo("SESSDATA=x");
				await Assert.That(config.Theme).IsEqualTo(AppTheme.Dark);
				await Assert.That(config.StreamHostSelection).IsEqualTo(StreamHostSelection.Random);
				await Assert.That(config.CodecPriority).IsEquivalentTo(StreamPriority.DefaultCodecs, CollectionOrdering.Matching);
				await Assert.That(config.FormatPriority).IsEquivalentTo([StreamFormat.Flv, StreamFormat.Ts], CollectionOrdering.Matching);
				await Assert.That(RoomIds(config)).IsEquivalentTo([732L, 6L, 7L], CollectionOrdering.Matching);

				await Assert.That((room.IsRecordingEnabled, room.IsNotificationEnabled, room.ConvertToMp4, room.DeleteAfterConvert)).IsEqualTo((true, true, true, null));
				await Assert.That(room.Quality).IsEqualTo(StreamQuality.BluRay);
				await Assert.That(room.CodecPriority).IsEquivalentTo([StreamCodec.Hevc, StreamCodec.Avc], CollectionOrdering.Matching);
				await Assert.That(room.StatusCheckInterval).IsEqualTo(TimeSpan.FromSeconds(60));
				await Assert.That(room.StreamIdleTimeout).IsEqualTo(TimeSpan.FromSeconds(30.5));
				await Assert.That(room.DanmuReconnectDelay).IsEqualTo(RoomDefaults.DanmuReconnectDelay);

				await Assert.That(disabled.IsRecordingEnabled).IsFalse();
				await Assert.That(disabled.Quality).IsEqualTo(new StreamQuality(12345));
				await Assert.That(disabled.FormatPriority).IsNull();

				await Assert.That(inheriting.StreamReconnectDelay).IsEqualTo(RoomDefaults.StreamReconnectDelay);
			}
		}

		await using AppConfigManager reloaded = Create();
		AppConfig saved = await reloaded.GetAsync(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(reloaded.IsLegacyConfigImported).IsFalse();
			await Assert.That(saved.Cookie).IsEqualTo("SESSDATA=x");
			await Assert.That(saved.Rooms).Count().IsEqualTo(3);
			await Assert.That(await File.ReadAllTextAsync(_directory.Combine(LegacyFileName), cancellationToken)).IsEqualTo(legacy);
		}
	}

	[Test]
	[Arguments(ConfigFileName, "{}", false)]
	[Arguments(ConfigFileName + ".bak", "{}", false)]
	[Arguments(ConfigFileName + ".tmp", "{}", false)]
	[Arguments(ConfigFileName + ".tmp", "{ truncated", true)]
	public async Task LegacyConfigIsOnlyImportedOnTheFirstRunAsync(string existing, string content, bool imported, CancellationToken cancellationToken)
	{
		await WriteFileAsync(existing, content, cancellationToken);
		await WriteFileAsync(LegacyFileName, """{ "Rooms": [{ "RoomId": 732 }] }""", cancellationToken);

		await using AppConfigManager manager = Create();
		AppConfig config = await manager.GetAsync(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(manager.IsLegacyConfigImported).IsEqualTo(imported);
			await Assert.That(RoomIds(config)).IsEquivalentTo(imported ? (long[])[732] : [], CollectionOrdering.Matching);
		}
	}

	[Test]
	[Arguments("{ truncated", """{ "Rooms": [{ "RoomId": 5 }] }""", 5L)]
	[Arguments("[]", null, null)]
	public async Task UnreadableLegacyConfigFallsBackToItsBackupAsync(string legacy, string? backup, long? expectedRoomId, CancellationToken cancellationToken)
	{
		await WriteFileAsync(LegacyFileName, legacy, cancellationToken);

		if (backup is not null)
		{
			await WriteFileAsync("BilibiliLiveRecordDownLoader.backup.json", backup, cancellationToken);
		}

		await using AppConfigManager manager = Create();
		AppConfig config = await manager.GetAsync(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(manager.IsLegacyConfigImported).IsEqualTo(expectedRoomId is not null);
			await Assert.That(File.Exists(ConfigPath)).IsEqualTo(expectedRoomId is not null);
			await Assert.That(config.Rooms.Select(static room => (long?)room.RoomId).SingleOrDefault()).IsEqualTo(expectedRoomId);
			await Assert.That(config.RecordDirectory).IsEqualTo(expectedRoomId is null ? ConfigurationConsts.DefaultRecordDirectory : LegacyDefaultRecordDirectory());
		}
	}

	[Test]
	public async Task RoomQualityIsReadFromEnumNamesAndNumbersAndSavedAsTheQnAsync(CancellationToken cancellationToken)
	{
		await WriteFileAsync
		(
			ConfigFileName,
			"""
			{
				"Rooms": [
					{ "RoomId": 1, "Quality": "Dolby" },
					{ "RoomId": 2, "Quality": "ultraHd4K" },
					{ "RoomId": 3, "Quality": 25000 },
					{ "RoomId": 4, "Quality": 12345 },
					{ "RoomId": 5, "Quality": "400" },
					{ "RoomId": 6, "Quality": "Original" },
					{ "RoomId": 7, "Quality": 0 }
				]
			}
			""",
			cancellationToken
		);

		IEnumerable<long> loaded;

		await using (AppConfigManager manager = Create())
		{
			loaded = (await manager.GetAsync(cancellationToken)).Rooms.Select(static room => room.Quality.Qn);
			await manager.UpdateAsync(static config => config.Theme = AppTheme.Dark, cancellationToken);
		}

		JsonArray rooms = (await ReadSavedAsync(cancellationToken))[nameof(AppConfig.Rooms)] as JsonArray ?? [];

		using (Assert.Multiple())
		{
			await Assert.That(loaded).IsEquivalentTo([30000L, 20000, 25000, 12345, 400, 10000, 0], CollectionOrdering.Matching);
			await Assert.That(rooms.Select(static room => room?[nameof(RoomConfig.Quality)]?.ToJsonString())).IsEquivalentTo(["30000", "20000", "25000", "12345", "400", null, "0"], CollectionOrdering.Matching);
		}
	}

	// 4.0 的实验开关，打开时原画的房间请求原画真彩
	[Test]
	[Arguments("true", new long[] { 25000, 400, 25000 }, DisplayName = "turned on")]
	[Arguments("false", new long[] { 10000, 400, 10000 }, DisplayName = "turned off")]
	[Arguments("\"yes\"", new long[] { 10000, 400, 10000 }, DisplayName = "unreadable")]
	public async Task RetiredRequestTrueOriginalSettingMovesOriginalRoomsToTheOriginalTrueColorOnceAsync(string requestTrueOriginal, long[] expected, CancellationToken cancellationToken)
	{
		await WriteFileAsync(ConfigFileName, $$"""{ "RequestTrueOriginal": {{requestTrueOriginal}}, "Theme": "Dark", "Rooms": [{ "RoomId": 1 }, { "RoomId": 2, "Quality": "BluRay" }, { "RoomId": 3, "Quality": 10000 }] }""", cancellationToken);

		AppConfig loaded = await ReloadAsync(cancellationToken);
		JsonObject saved = await ReadSavedAsync(cancellationToken);

		await using (AppConfigManager manager = Create())
		{
			await manager.UpdateAsync(static config => config.Rooms = [.. config.Rooms.Select(static room => room.RoomId is 1 ? room with { Quality = StreamQuality.Original } : room)], cancellationToken);
		}

		AppConfig changed = await ReloadAsync(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(loaded.Rooms.Select(static room => room.Quality.Qn)).IsEquivalentTo(expected, CollectionOrdering.Matching);
			await Assert.That(loaded.Theme).IsEqualTo(AppTheme.Dark);
			await Assert.That(saved.ContainsKey("RequestTrueOriginal")).IsEqualTo(requestTrueOriginal is not "true");
			await Assert.That(changed.Rooms.Select(static room => room.Quality.Qn)).IsEquivalentTo([10000L, .. expected.Skip(1)], CollectionOrdering.Matching);
			await Assert.That((await ReadSavedAsync(cancellationToken)).ContainsKey("RequestTrueOriginal")).IsFalse();
		}
	}

	// 4.0 的实验开关，现在画质受限时总是改用主播放列表
	[Test]
	public async Task RetiredMasterPlaylistSettingIsIgnoredAndDroppedAsync(CancellationToken cancellationToken)
	{
		await WriteFileAsync(ConfigFileName, """{ "UseMasterPlaylistWhenRestricted": false, "Theme": "Dark" }""", cancellationToken);

		await using (AppConfigManager manager = Create())
		{
			await Assert.That((await manager.GetAsync(cancellationToken)).Theme).IsEqualTo(AppTheme.Dark);
			await manager.UpdateAsync(static config => config.RemoveFillerData = true, cancellationToken);
		}

		await Assert.That((await ReadSavedAsync(cancellationToken)).Select(static property => property.Key)).IsEquivalentTo([nameof(AppConfig.Theme), nameof(AppConfig.RemoveFillerData)], CollectionOrdering.Matching);
	}

	// 录制方式已经删掉，所有录制都走内置录制；旧配置里全局和房间的这一项都不影响读取
	[Test]
	public async Task RetiredRecorderTypeSettingIsIgnoredAndDroppedAsync(CancellationToken cancellationToken)
	{
		await WriteFileAsync(ConfigFileName, """{ "RecorderType": "FFmpeg", "Theme": "Dark", "Rooms": [{ "RoomId": 1, "RecorderType": "FFmpeg" }] }""", cancellationToken);

		await using (AppConfigManager manager = Create())
		{
			AppConfig config = await manager.GetAsync(cancellationToken);
			await Assert.That((config.Theme, RoomIds(config).Single())).IsEqualTo((AppTheme.Dark, 1L));
			await manager.UpdateAsync(static config => config.RemoveFillerData = true, cancellationToken);
		}

		JsonObject saved = await ReadSavedAsync(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(saved.Select(static property => property.Key)).IsEquivalentTo([nameof(AppConfig.Theme), nameof(AppConfig.Rooms), nameof(AppConfig.RemoveFillerData)]);
			await Assert.That(SavedRoom(saved).Select(static property => property.Key)).IsEquivalentTo([nameof(RoomConfig.RoomId)]);
		}
	}

	private static JsonObject SavedRoom(JsonObject saved)
	{
		return saved[nameof(AppConfig.Rooms)]?[0] as JsonObject ?? [];
	}

	private static IEnumerable<long> RoomIds(AppConfig config)
	{
		return config.Rooms.Select(static room => room.RoomId);
	}

	private static string LegacyDefaultRecordDirectory()
	{
		return Environment.GetFolderPath(Environment.SpecialFolder.MyVideos) is { Length: > 0 } videos ? videos : ConfigurationConsts.DefaultRecordDirectory;
	}

	private AppConfigManager Create(TimeSpan? saveDelay = null, ILogger<AppConfigManager>? logger = null, TimeProvider? time = null)
	{
		AppConfigStorageOptions options = new() { FilePath = ConfigPath, LegacyFilePath = _directory.Combine(LegacyFileName), SaveDelay = saveDelay ?? TimeSpan.Zero };
		return new AppConfigManager(Options.Create(options), time ?? TimeProvider.System, logger ?? NullLogger<AppConfigManager>.Instance);
	}

	private async Task<AppConfig> ReloadAsync(CancellationToken cancellationToken)
	{
		await using AppConfigManager manager = Create();
		return await manager.GetAsync(cancellationToken);
	}

	private async Task<JsonObject> ReadSavedAsync(CancellationToken cancellationToken)
	{
		return JsonNode.Parse(await File.ReadAllTextAsync(ConfigPath, cancellationToken)) as JsonObject ?? [];
	}

	private Task WriteFileAsync(string fileName, string content, CancellationToken cancellationToken)
	{
		return File.WriteAllTextAsync(_directory.Combine(fileName), content, cancellationToken);
	}

	private string[] CorruptedFiles()
	{
		return Directory.GetFiles(_directory.Root, ConfigFileName + ".corrupt-*");
	}

	private sealed class CancelOnErrorLogger(CancellationTokenSource cancellation) : ILogger<AppConfigManager>
	{
		public IDisposable? BeginScope<TState>(TState state) where TState : notnull
		{
			return null;
		}

		public bool IsEnabled(LogLevel logLevel)
		{
			return true;
		}

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
		{
			if (logLevel is LogLevel.Error)
			{
				cancellation.Cancel();
			}
		}
	}
}
