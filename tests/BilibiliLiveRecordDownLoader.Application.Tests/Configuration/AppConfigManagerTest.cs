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

		using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(TimeSpan.FromSeconds(10));

		while (!File.Exists(path))
		{
			await Task.Delay(10, timeout.Token);
		}

		await using AppConfigManager reloaded = Create(path);
		await Assert.That((await reloaded.GetAsync(cancellationToken)).Language).IsEqualTo("zh-Hans");
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

		// 恢复的内容立即写回 config.json，下次启动不依赖备份
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

	private static JsonObject SavedRoom(JsonObject saved)
	{
		return saved[nameof(AppConfig.Rooms)]?[0] as JsonObject ?? [];
	}

	private static AppConfigManager Create(string path, TimeSpan? saveDelay = null)
	{
		return new AppConfigManager(Options.Create(new AppConfigStorageOptions { FilePath = path, SaveDelay = saveDelay ?? TimeSpan.Zero }), NullLogger<AppConfigManager>.Instance);
	}

	private static bool AreEquivalent(AppConfig x, AppConfig y)
	{
		// 记录相等对列表只比较引用，列表单独按元素比较
		return x with { CodecPriority = y.CodecPriority, FormatPriority = y.FormatPriority, Rooms = y.Rooms } == y
			&& x.CodecPriority.SequenceEqual(y.CodecPriority)
			&& x.FormatPriority.SequenceEqual(y.FormatPriority)
			&& x.Rooms.SequenceEqual(y.Rooms);
	}
}
