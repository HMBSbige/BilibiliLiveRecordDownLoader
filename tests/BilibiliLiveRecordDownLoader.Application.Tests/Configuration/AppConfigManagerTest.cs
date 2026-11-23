using TUnit.Assertions.Enums;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Configuration;

public sealed class AppConfigManagerTest
{
	[Test]
	public async Task MissingFileLoadsDefaultsAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		await using AppConfigManager manager = Create(directory.Combine("config.json"));

		AppConfig config = await manager.GetAsync(cancellationToken);

		await Assert.That(AreEquivalent(config, AppConfig.CreateDefault())).IsTrue();
	}

	[Test]
	public async Task UpdatesArePersistedWithoutDefaultValuesAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string path = directory.Combine("config.json");

		await using (AppConfigManager manager = Create(path))
		{
			await manager.UpdateAsync(static config =>
			{
				config.Theme = AppTheme.Dark;
				config.Rooms.Add(new RoomConfig { RoomId = 732, RecorderType = RecorderType.FFmpeg });
			}, cancellationToken);
		}

		JsonObject saved = JsonNode.Parse(await File.ReadAllTextAsync(path, cancellationToken)) as JsonObject ?? [];

		await Assert.That(saved["Theme"]?.GetValue<string>()).IsEqualTo(nameof(AppTheme.Dark));
		await Assert.That(saved.ContainsKey(nameof(AppConfig.AutoCheckUpdate))).IsFalse();
		await Assert.That(saved.ContainsKey(nameof(AppConfig.RecordDirectory))).IsFalse();
		await Assert.That(string.Join(", ", SavedRoom(saved).Select(static property => property.Key))).IsEqualTo($"{nameof(RoomConfig.RoomId)}, {nameof(RoomConfig.RecorderType)}");

		await using AppConfigManager reloaded = Create(path);
		AppConfig config = await reloaded.GetAsync(cancellationToken);

		await Assert.That(config.Theme).IsEqualTo(AppTheme.Dark);
		await Assert.That(config.Rooms.Count).IsEqualTo(1);
		await Assert.That(config.Rooms[0].RoomId).IsEqualTo(732);
		await Assert.That(config.Rooms[0].RecorderType).IsEqualTo(RecorderType.FFmpeg);
		await Assert.That(config.Rooms[0].StatusCheckInterval).IsEqualTo(new RoomConfig().StatusCheckInterval);
	}

	[Test]
	[Arguments(true, false, "")]
	[Arguments(false, true, $"{nameof(AppConfig.NotifyOnRecordingError)}, {nameof(AppConfig.NotifyOnTitleChange)}")]
	public async Task NotificationSwitchesAreSavedOnlyWhenChangedFromTheirDefaultsAsync(bool notifyOnRecordingError, bool notifyOnTitleChange, string expectedKeys, CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string path = directory.Combine("config.json");

		await using (AppConfigManager manager = Create(path))
		{
			await manager.UpdateAsync(config =>
			{
				config.NotifyOnRecordingError = notifyOnRecordingError;
				config.NotifyOnTitleChange = notifyOnTitleChange;
			}, cancellationToken);
		}

		JsonObject saved = JsonNode.Parse(await File.ReadAllTextAsync(path, cancellationToken)) as JsonObject ?? [];
		await using AppConfigManager reloaded = Create(path);
		AppConfig config = await reloaded.GetAsync(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(string.Join(", ", saved.Select(static property => property.Key).Where(static key => key.StartsWith("Notify", StringComparison.Ordinal)))).IsEqualTo(expectedKeys);
			await Assert.That((config.NotifyOnRecordingError, config.NotifyOnTitleChange)).IsEqualTo((notifyOnRecordingError, notifyOnTitleChange));
		}
	}

	// 配置文件里已废弃的 TryOriginalWhenRestricted 键不影响这个开关
	[Test]
	public async Task MasterPlaylistIsOnInAnOlderConfigAndSavedOnceTurnedOffAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string path = directory.Combine("config.json");
		await File.WriteAllTextAsync(path, """{ "Theme": "Dark", "TryOriginalWhenRestricted": false }""", cancellationToken);
		bool loaded;

		await using (AppConfigManager manager = Create(path))
		{
			loaded = (await manager.GetAsync(cancellationToken)).UseMasterPlaylistWhenRestricted;
			await manager.UpdateAsync(static config => config.UseMasterPlaylistWhenRestricted = false, cancellationToken);
		}

		JsonObject saved = JsonNode.Parse(await File.ReadAllTextAsync(path, cancellationToken)) as JsonObject ?? [];
		await using AppConfigManager reloaded = Create(path);

		using (Assert.Multiple())
		{
			await Assert.That(loaded).IsTrue();
			await Assert.That(saved[nameof(AppConfig.UseMasterPlaylistWhenRestricted)]?.GetValue<bool>()).IsFalse();
			await Assert.That((await reloaded.GetAsync(cancellationToken)).UseMasterPlaylistWhenRestricted).IsFalse();
		}
	}

	[Test]
	public async Task SplitModesAreSavedAsNamesAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string path = directory.Combine("config.json");

		await using (AppConfigManager manager = Create(path))
		{
			await manager.UpdateAsync(static config =>
			{
				config.SplitMode = RecordingSplitMode.Size;
				config.Rooms.Add(new RoomConfig { RoomId = 732, SplitMode = RecordingSplitMode.Duration, SplitDuration = TimeSpan.FromMinutes(30) });
			}, cancellationToken);
		}

		JsonObject saved = JsonNode.Parse(await File.ReadAllTextAsync(path, cancellationToken)) as JsonObject ?? [];
		await using AppConfigManager reloaded = Create(path);
		AppConfig config = await reloaded.GetAsync(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(saved[nameof(AppConfig.SplitMode)]?.GetValue<string>()).IsEqualTo(nameof(RecordingSplitMode.Size));
			await Assert.That(SavedRoom(saved)[nameof(RoomConfig.SplitMode)]?.GetValue<string>()).IsEqualTo(nameof(RecordingSplitMode.Duration));
			await Assert.That(config.SplitMode).IsEqualTo(RecordingSplitMode.Size);
			await Assert.That(config.SplitSizeMegabytes).IsEqualTo(RecordingSplitDefaults.SizeMegabytes);
			await Assert.That(config.Rooms[0].SplitMode).IsEqualTo(RecordingSplitMode.Duration);
			await Assert.That(config.Rooms[0].SplitDuration).IsEqualTo(TimeSpan.FromMinutes(30));
			await Assert.That(config.Rooms[0].SplitSizeMegabytes).IsNull();
		}
	}

	[Test]
	public async Task RoomsSavedWithEveryValueAreTrimmedOnTheNextSaveAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string path = directory.Combine("config.json");
		await File.WriteAllTextAsync(path, """
			{
				"Rooms": [
					{
						"RoomId": 732,
						"IsRecordingEnabled": true,
						"IsNotificationEnabled": false,
						"Quality": "Original",
						"StatusCheckInterval": "00:05:00",
						"DanmuReconnectDelay": "00:00:02",
						"StreamReconnectDelay": "00:00:06",
						"StreamConnectTimeout": "00:00:05",
						"StreamIdleTimeout": "00:00:30"
					}
				]
			}
			""", cancellationToken);

		await using (AppConfigManager manager = Create(path))
		{
			RoomConfig room = (await manager.GetAsync(cancellationToken)).Rooms.Single();
			await Assert.That(room).IsEqualTo(new RoomConfig { RoomId = 732, StreamIdleTimeout = TimeSpan.FromSeconds(30) });

			await manager.UpdateAsync(static config => config.Theme = AppTheme.Dark, cancellationToken);
		}

		JsonObject saved = JsonNode.Parse(await File.ReadAllTextAsync(path, cancellationToken)) as JsonObject ?? [];

		await Assert.That(string.Join(", ", SavedRoom(saved).Select(static property => $"{property.Key}={property.Value}"))).IsEqualTo("RoomId=732, StreamIdleTimeout=00:00:30");
	}

	[Test]
	public async Task PendingChangesAreFlushedOnDisposeAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string path = directory.Combine("config.json");

		AppConfigManager manager = Create(path, TimeSpan.FromMinutes(1));
		await manager.UpdateAsync(static config => config.Language = "zh-Hans", cancellationToken);
		await Assert.That(File.Exists(path)).IsFalse();

		manager.Dispose();

		await using AppConfigManager reloaded = Create(path);
		await Assert.That((await reloaded.GetAsync(cancellationToken)).Language).IsEqualTo("zh-Hans");
	}

	[Test]
	public async Task CallerCancellationDoesNotDropTheDebouncedSaveAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string path = directory.Combine("config.json");
		await using AppConfigManager manager = Create(path, TimeSpan.FromMilliseconds(50));

		using (CancellationTokenSource caller = new())
		{
			await manager.UpdateAsync(static config => config.Language = "zh-Hans", caller.Token);
			await caller.CancelAsync();
		}

		await TestWait.UntilAsync(() => File.Exists(path), "the update is saved although its caller gave up", cancellationToken, TimeSpan.FromSeconds(10));

		await using AppConfigManager reloaded = Create(path);
		await Assert.That((await reloaded.GetAsync(cancellationToken)).Language).IsEqualTo("zh-Hans");
	}

	[Test]
	[Arguments("""{ "Theme": "Dark", "Rooms": null }""", "")]
	[Arguments("""{ "Theme": "Dark", "Rooms": [null, { "RoomId": 1 }] }""", "1")]
	public async Task NullRoomsAreSkippedAsync(string json, string expectedRooms, CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string path = directory.Combine("config.json");
		await File.WriteAllTextAsync(path, json, cancellationToken);

		await using AppConfigManager manager = Create(path);
		AppConfig config = await manager.GetAsync(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(config.Theme).IsEqualTo(AppTheme.Dark);
			await Assert.That(string.Join(",", config.Rooms.Select(static room => room.RoomId))).IsEqualTo(expectedRooms);
		}
	}

	[Test]
	public async Task UnreadableValuesFallBackToTheirDefaultsWithoutDiscardingTheRestAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string path = directory.Combine("config.json");
		await File.WriteAllTextAsync(path, """
			{
				// 手改的配置
				"theme": "Future",
				"Cookie": "a=1",
				"CodecPriority": ["Hevc", "Future", "Avc"],
				"FormatPriority": ["Future"],
				"SplitDuration": 42,
				"Rooms": [{ "RoomId": 1, "Quality": "Future", "IsNotificationEnabled": true, "FormatPriority": ["Ts", "Future", null] }, 5, { "RoomId": 2 }],
			}
			""", cancellationToken);

		await using (AppConfigManager manager = Create(path))
		{
			AppConfig config = await manager.GetAsync(cancellationToken);

			using (Assert.Multiple())
			{
				await Assert.That(config.Theme).IsEqualTo(AppConfig.CreateDefault().Theme);
				await Assert.That(config.Cookie).IsEqualTo("a=1");
				await Assert.That(config.CodecPriority).IsEquivalentTo([StreamCodec.Hevc, StreamCodec.Avc], CollectionOrdering.Matching);
				await Assert.That(config.FormatPriority).IsEquivalentTo(StreamPriority.DefaultFormats, CollectionOrdering.Matching);
				await Assert.That(config.Rooms[0].FormatPriority).IsEquivalentTo([StreamFormat.Ts], CollectionOrdering.Matching);
				await Assert.That(config.SplitDuration).IsEqualTo(RecordingSplitDefaults.Duration);
				await Assert.That(config.Rooms.Select(static room => room.RoomId).SequenceEqual([1L, 2L])).IsTrue();
				await Assert.That((config.Rooms[0].Quality, config.Rooms[0].IsNotificationEnabled)).IsEqualTo((RoomDefaults.Quality, true));
				await Assert.That(Directory.GetFiles(directory.Root, "config.json.corrupt-*")).IsEmpty();
			}
		}

		// 清理后的内容立即写回
		JsonObject saved = JsonNode.Parse(await File.ReadAllTextAsync(path, cancellationToken)) as JsonObject ?? [];
		await Assert.That(saved.ContainsKey("theme") || saved.ContainsKey(nameof(AppConfig.Theme))).IsFalse();
	}

	[Test]
	public async Task UpdatesAreClampedToTheirBoundsAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		await using AppConfigManager manager = Create(directory.Combine("config.json"));

		await manager.UpdateAsync(static config =>
		{
			config.DanmakuOffset = TimeSpan.FromMinutes(10);
			config.SplitDuration = TimeSpan.Zero;
		}, cancellationToken);
		AppConfig config = await manager.GetAsync(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(config.DanmakuOffset).IsEqualTo(DanmakuDefaults.MaxOffset);
			await Assert.That(config.SplitDuration).IsEqualTo(RecordingSplitDefaults.MinDuration);
		}
	}

	[Test]
	public async Task CorruptedFileFallsBackToDefaultsAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string path = directory.Combine("config.json");
		await File.WriteAllTextAsync(path, "{ not json", cancellationToken);

		await using AppConfigManager manager = Create(path);
		AppConfig config = await manager.GetAsync(cancellationToken);

		await Assert.That(config.RecordDirectory).IsEqualTo(ConfigurationConsts.DefaultRecordDirectory);
		await Assert.That(config.Rooms).IsEmpty();
		await Assert.That(Directory.GetFiles(directory.Root, "config.json.corrupt-*").Length).IsEqualTo(1);
	}

	[Test]
	public async Task CorruptedFileIsRestoredFromBackupAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string path = directory.Combine("config.json");

		await using (AppConfigManager manager = Create(path))
		{
			await manager.UpdateAsync(static config => config.Rooms.Add(new RoomConfig { RoomId = 732 }), cancellationToken);
		}

		await using (AppConfigManager manager = Create(path))
		{
			await manager.UpdateAsync(static config => config.Theme = AppTheme.Dark, cancellationToken);
		}

		await File.WriteAllTextAsync(path, "{ truncated", cancellationToken);

		await using (AppConfigManager manager = Create(path))
		{
			AppConfig restored = await manager.GetAsync(cancellationToken);

			await Assert.That(restored.Rooms.Select(static room => room.RoomId).SequenceEqual([732L])).IsTrue();
		}

		// 恢复的内容立即写回 config.json
		await using AppConfigManager reloaded = Create(path);
		await Assert.That((await reloaded.GetAsync(cancellationToken)).Rooms.Count).IsEqualTo(1);
		await Assert.That(Directory.GetFiles(directory.Root, "config.json.corrupt-*").Length).IsEqualTo(1);
	}

	[Test]
	[Arguments("""{ "Rooms": [{ "RoomId": 732 }] }""", 732L)]
	[Arguments("{ truncated", 1L)]
	public async Task InterruptedSaveIsRestoredFromTheTempFileThenTheBackupAsync(string temp, long expectedRoomId, CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string path = directory.Combine("config.json");
		await File.WriteAllTextAsync(path + ".tmp", temp, cancellationToken);
		await File.WriteAllTextAsync(path + ".bak", """{ "Rooms": [{ "RoomId": 1 }] }""", cancellationToken);

		await using (AppConfigManager manager = Create(path))
		{
			AppConfig restored = await manager.GetAsync(cancellationToken);

			await Assert.That(restored.Rooms.Select(static room => room.RoomId).SequenceEqual([expectedRoomId])).IsTrue();
		}

		JsonObject saved = JsonNode.Parse(await File.ReadAllTextAsync(path, cancellationToken)) as JsonObject ?? [];

		using (Assert.Multiple())
		{
			await Assert.That(SavedRoom(saved)[nameof(RoomConfig.RoomId)]?.GetValue<long>()).IsEqualTo(expectedRoomId);
			await Assert.That(File.Exists(path + ".tmp")).IsFalse();
		}
	}

	[Test]
	public async Task DeletedConfigIsNotRestoredFromTheBackupAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string path = directory.Combine("config.json");
		await File.WriteAllTextAsync(path + ".bak", """{ "Rooms": [{ "RoomId": 1 }] }""", cancellationToken);

		await using AppConfigManager manager = Create(path);
		AppConfig config = await manager.GetAsync(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(config.Rooms).IsEmpty();
			await Assert.That(File.Exists(path)).IsFalse();
		}
	}

	[Test]
	public async Task LegacyConfigIsImportedAndSavedWithoutTouchingTheOldFileAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string path = directory.Combine("config.json");
		string legacyPath = directory.Combine("BilibiliLiveRecordDownLoader.json");
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
		await File.WriteAllTextAsync(legacyPath, legacy, cancellationToken);

		await using (AppConfigManager manager = Create(path, legacyPath: legacyPath))
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
				await Assert.That(config.RecorderType).IsEqualTo(RecorderType.FFmpeg);
				await Assert.That(config.StreamHostSelection).IsEqualTo(StreamHostSelection.Random);
				await Assert.That(config.CodecPriority).IsEquivalentTo(StreamPriority.DefaultCodecs, CollectionOrdering.Matching);
				await Assert.That(config.FormatPriority).IsEquivalentTo([StreamFormat.Flv, StreamFormat.Ts], CollectionOrdering.Matching);
				await Assert.That(config.Rooms.Select(static room => room.RoomId)).IsEquivalentTo([732L, 6L, 7L], CollectionOrdering.Matching);

				await Assert.That((room.IsRecordingEnabled, room.IsNotificationEnabled, room.ConvertToMp4, room.DeleteAfterConvert)).IsEqualTo((true, true, true, null));
				await Assert.That(room.Quality).IsEqualTo(StreamQuality.BluRay);
				await Assert.That(room.RecorderType).IsEqualTo(RecorderType.FFmpeg);
				await Assert.That(room.CodecPriority).IsEquivalentTo([StreamCodec.Hevc, StreamCodec.Avc], CollectionOrdering.Matching);
				await Assert.That(room.StatusCheckInterval).IsEqualTo(TimeSpan.FromSeconds(60));
				await Assert.That(room.StreamIdleTimeout).IsEqualTo(TimeSpan.FromSeconds(30.5));
				await Assert.That(room.DanmuReconnectDelay).IsEqualTo(RoomDefaults.DanmuReconnectDelay);

				await Assert.That(disabled.IsRecordingEnabled).IsFalse();
				await Assert.That(disabled.Quality).IsEqualTo(RoomDefaults.Quality);
				await Assert.That(disabled.RecorderType).IsEqualTo(RecorderType.BuiltIn);
				await Assert.That(disabled.FormatPriority).IsNull();

				await Assert.That(inheriting.RecorderType).IsNull();
				await Assert.That(inheriting.StreamReconnectDelay).IsEqualTo(RoomDefaults.StreamReconnectDelay);
			}
		}

		await using AppConfigManager reloaded = Create(path, legacyPath: legacyPath);
		AppConfig saved = await reloaded.GetAsync(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(reloaded.IsLegacyConfigImported).IsFalse();
			await Assert.That(saved.Cookie).IsEqualTo("SESSDATA=x");
			await Assert.That(saved.Rooms.Count).IsEqualTo(3);
			await Assert.That(await File.ReadAllTextAsync(legacyPath, cancellationToken)).IsEqualTo(legacy);
		}
	}

	[Test]
	[Arguments("config.json")]
	[Arguments("config.json.bak")]
	public async Task LegacyConfigIsOnlyImportedOnTheFirstRunAsync(string existing, CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string path = directory.Combine("config.json");
		string legacyPath = directory.Combine("BilibiliLiveRecordDownLoader.json");
		await File.WriteAllTextAsync(directory.Combine(existing), "{}", cancellationToken);
		await File.WriteAllTextAsync(legacyPath, """{ "Rooms": [{ "RoomId": 732 }] }""", cancellationToken);

		await using AppConfigManager manager = Create(path, legacyPath: legacyPath);
		AppConfig config = await manager.GetAsync(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(manager.IsLegacyConfigImported).IsFalse();
			await Assert.That(config.Rooms).IsEmpty();
		}
	}

	[Test]
	[Arguments("{ truncated", """{ "Rooms": [{ "RoomId": 5 }] }""", 5L)]
	[Arguments("[]", null, null)]
	public async Task UnreadableLegacyConfigFallsBackToItsBackupAsync(string legacy, string? backup, long? expectedRoomId, CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string path = directory.Combine("config.json");
		string legacyPath = directory.Combine("BilibiliLiveRecordDownLoader.json");
		await File.WriteAllTextAsync(legacyPath, legacy, cancellationToken);

		if (backup is not null)
		{
			await File.WriteAllTextAsync(directory.Combine("BilibiliLiveRecordDownLoader.backup.json"), backup, cancellationToken);
		}

		await using AppConfigManager manager = Create(path, legacyPath: legacyPath);
		AppConfig config = await manager.GetAsync(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(manager.IsLegacyConfigImported).IsEqualTo(expectedRoomId is not null);
			await Assert.That(File.Exists(path)).IsEqualTo(expectedRoomId is not null);
			await Assert.That(config.Rooms.Select(static room => (long?)room.RoomId).SingleOrDefault()).IsEqualTo(expectedRoomId);
			// 旧版没写录制目录时录到“视频”文件夹本身
			await Assert.That(config.RecordDirectory).IsEqualTo(expectedRoomId is null ? ConfigurationConsts.DefaultRecordDirectory : LegacyDefaultRecordDirectory());
		}
	}

	private static JsonObject SavedRoom(JsonObject saved)
	{
		return saved[nameof(AppConfig.Rooms)]?[0] as JsonObject ?? [];
	}

	private static AppConfigManager Create(string path, TimeSpan? saveDelay = null, string? legacyPath = null)
	{
		return new AppConfigManager(Options.Create(new AppConfigStorageOptions { FilePath = path, LegacyFilePath = legacyPath, SaveDelay = saveDelay ?? TimeSpan.Zero }), NullLogger<AppConfigManager>.Instance);
	}

	private static string LegacyDefaultRecordDirectory()
	{
		return Environment.GetFolderPath(Environment.SpecialFolder.MyVideos) is { Length: > 0 } videos ? videos : ConfigurationConsts.DefaultRecordDirectory;
	}

	private static bool AreEquivalent(AppConfig x, AppConfig y)
	{
		// 记录相等对列表只比较引用
		return x with { CodecPriority = y.CodecPriority, FormatPriority = y.FormatPriority, Rooms = y.Rooms } == y
			&& x.CodecPriority.SequenceEqual(y.CodecPriority)
			&& x.FormatPriority.SequenceEqual(y.FormatPriority)
			&& x.Rooms.SequenceEqual(y.Rooms);
	}
}
