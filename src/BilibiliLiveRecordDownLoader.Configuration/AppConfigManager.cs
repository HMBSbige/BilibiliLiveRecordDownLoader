using System.Text.Json.Serialization.Metadata;

namespace BilibiliLiveRecordDownLoader.Configuration;

public sealed partial class AppConfigManager(IOptions<AppConfigStorageOptions> storageOptions, ILogger<AppConfigManager> logger) : IAppConfigManager, IDisposable, IAsyncDisposable
{
	private const int ReadAttempts = 3;
	private const UnixFileMode PrivateFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
	private const UnixFileMode PrivateDirectoryMode = PrivateFileMode | UnixFileMode.UserExecute;

	private static readonly TimeSpan ReadRetryDelay = TimeSpan.FromMilliseconds(200);
	private static readonly JsonDocumentOptions DocumentOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
	private static readonly JsonObject DefaultNode = SerializeToJsonObject(AppConfig.CreateDefault());
	private static readonly JsonObject DefaultRoomNode = JsonSerializer.SerializeToNode(new RoomConfig(), AppConfigJsonContext.Default.RoomConfig) as JsonObject ?? throw new InvalidOperationException("Serialized room config is not a JSON object.");

	private readonly string _configPath = storageOptions.Value.FilePath;
	private readonly TimeSpan _saveDelay = storageOptions.Value.SaveDelay;
	private readonly SemaphoreSlim _lock = new(1, 1);
	private readonly CancellationTokenSource _disposal = new();
	private long _saveVersion;
	private long _savedVersion;
	private bool _isSaveScheduled;
	private bool _isReadOnly;
	private bool _disposed;
	private AppConfig? _config;

	private string BackupPath => _configPath + ".bak";

	private string TempPath => _configPath + ".tmp";

	public async ValueTask<AppConfig> GetAsync(CancellationToken cancellationToken = default)
	{
		await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);

		try
		{
			return await GetOrLoadCoreAsync(cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			_lock.Release();
		}
	}

	// 写盘延迟合并，调用方取消不会丢掉已做的修改；取值范围在这里统一限制
	public async ValueTask UpdateAsync(Action<AppConfig> update, CancellationToken cancellationToken = default)
	{
		await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);

		try
		{
			ObjectDisposedException.ThrowIf(_disposed, this);
			AppConfig config = await GetOrLoadCoreAsync(cancellationToken).ConfigureAwait(false);
			update(config);
			config.ApplyDefaults();
			++_saveVersion;

			if (!_isSaveScheduled)
			{
				_isSaveScheduled = true;
				_ = SaveAfterDelayAsync(_disposal.Token);
			}
		}
		finally
		{
			_lock.Release();
		}
	}

	// 退出时同步写盘：调用线程的同步上下文可能已经不再调度
	public void Dispose()
	{
		_disposal.Cancel();
		_lock.Wait();

		try
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			SaveCore();
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			LogSaveFailed(logger, exception, _configPath);
		}
		finally
		{
			_lock.Release();
		}
	}

	public ValueTask DisposeAsync()
	{
		Dispose();
		return ValueTask.CompletedTask;
	}

	private async Task SaveAfterDelayAsync(CancellationToken cancellationToken)
	{
		try
		{
			await Task.Delay(_saveDelay, cancellationToken).ConfigureAwait(false);
			await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);

			try
			{
				_isSaveScheduled = false;
				SaveCore();
			}
			finally
			{
				_lock.Release();
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			// Dispose 会负责写盘
		}
		catch (Exception exception)
		{
			LogSaveFailed(logger, exception, _configPath);
		}
	}

	private async ValueTask<AppConfig> GetOrLoadCoreAsync(CancellationToken cancellationToken)
	{
		if (_config is not null)
		{
			return _config;
		}

		(_config, bool restored) = await LoadCoreAsync(cancellationToken).ConfigureAwait(false);

		if (restored)
		{
			++_saveVersion;

			try
			{
				SaveCore();
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				LogSaveFailed(logger, exception, _configPath);
			}
		}

		return _config;
	}

	private async ValueTask<(AppConfig Config, bool Restored)> LoadCoreAsync(CancellationToken cancellationToken)
	{
		AppConfig? config = null;
		bool restored = false;

		try
		{
			config = await ReadWithRetryAsync(_configPath, cancellationToken).ConfigureAwait(false);

			// 上次保存在替换文件时中断，新内容还留在临时文件里；只剩备份时视为用户删除配置来重置
			if (config is null && File.Exists(TempPath))
			{
				config = await TryRestoreAsync(TempPath, cancellationToken).ConfigureAwait(false) ?? await TryRestoreAsync(BackupPath, cancellationToken).ConfigureAwait(false);
				restored = config is not null;
			}
		}
		catch (JsonException exception)
		{
			config = await TryReadSkippingInvalidValuesAsync(cancellationToken).ConfigureAwait(false);

			if (config is null)
			{
				LogConfigCorrupted(logger, exception, _configPath);
				PreserveCorruptedFile();
				config = await TryRestoreAsync(BackupPath, cancellationToken).ConfigureAwait(false);
			}

			restored = config is not null;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			// 读不到时不能用默认值覆盖用户的配置
			LogConfigUnreadable(logger, exception, _configPath);
			_isReadOnly = true;
		}

		config ??= new AppConfig();
		config.ApplyDefaults();
		RestrictPermissions();

		return (config, restored);
	}

	private static async ValueTask<AppConfig?> ReadWithRetryAsync(string path, CancellationToken cancellationToken)
	{
		for (int attempt = 1; ; ++attempt)
		{
			try
			{
				return await ReadAsync(path, cancellationToken).ConfigureAwait(false);
			}
			catch (IOException) when (attempt < ReadAttempts)
			{
				// 杀毒或同步软件可能短暂占用文件
				await Task.Delay(ReadRetryDelay, cancellationToken).ConfigureAwait(false);
			}
		}
	}

	private static async ValueTask<AppConfig?> ReadAsync(string path, CancellationToken cancellationToken)
	{
		if (!File.Exists(path))
		{
			return null;
		}

		using FileStream stream = File.OpenRead(path);
		return await JsonSerializer.DeserializeAsync(stream, AppConfigJsonContext.Default.AppConfig, cancellationToken).ConfigureAwait(false);
	}

	// 新版本可能新增了枚举名，手改也可能写错一个值；只让读不出来的值回到默认值，不让整份配置作废
	private async ValueTask<AppConfig?> TryReadSkippingInvalidValuesAsync(CancellationToken cancellationToken)
	{
		try
		{
			JsonNode? node;

			using (FileStream stream = File.OpenRead(_configPath))
			{
				node = await JsonNode.ParseAsync(stream, documentOptions: DocumentOptions, cancellationToken: cancellationToken).ConfigureAwait(false);
			}

			if (node is not JsonObject root)
			{
				return null;
			}

			RemoveInvalidValues(root, AppConfigJsonContext.Default.AppConfig, string.Empty);
			return root.Deserialize(AppConfigJsonContext.Default.AppConfig);
		}
		catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
		{
			return null;
		}
	}

	private void RemoveInvalidValues(JsonObject node, JsonTypeInfo typeInfo, string prefix)
	{
		foreach ((string key, JsonNode? value) in node.ToArray())
		{
			// 未知的属性读取时本来就会忽略
			if (typeInfo.Properties.FirstOrDefault(property => string.Equals(property.Name, key, StringComparison.OrdinalIgnoreCase)) is not { } property)
			{
				continue;
			}

			string name = prefix + property.Name;

			if (property.PropertyType == typeof(List<RoomConfig>) && value is JsonArray rooms)
			{
				for (int i = rooms.Count - 1; i >= 0; i--)
				{
					if (rooms[i] is JsonObject room)
					{
						RemoveInvalidValues(room, AppConfigJsonContext.Default.RoomConfig, $"{name}[{i}].");
					}
					else
					{
						LogInvalidValue(logger, $"{name}[{i}]", _configPath);
						rooms.RemoveAt(i);
					}
				}
			}

			// 编码和格式的优先级里一个值读不出时只丢这一个，其余的顺序还是用户排的
			if (value is JsonArray items && GetEnumListElementType(property.PropertyType) is { } elementType && AppConfigJsonContext.Default.GetTypeInfo(elementType) is { } elementTypeInfo)
			{
				for (int i = items.Count - 1; i >= 0; i--)
				{
					if (!CanRead(items[i], elementTypeInfo))
					{
						LogInvalidValue(logger, $"{name}[{i}]", _configPath);
						items.RemoveAt(i);
					}
				}
			}

			if (AppConfigJsonContext.Default.GetTypeInfo(property.PropertyType) is not { } propertyTypeInfo)
			{
				continue;
			}

			if (!CanRead(value, propertyTypeInfo))
			{
				LogInvalidValue(logger, name, _configPath);
				node.Remove(key);
			}
		}
	}

	private static Type? GetEnumListElementType(Type type)
	{
		return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>) && type.GetGenericArguments()[0] is { IsEnum: true } elementType ? elementType : null;
	}

	private static bool CanRead(JsonNode? value, JsonTypeInfo typeInfo)
	{
		try
		{
			_ = value.Deserialize(typeInfo);
			return true;
		}
		catch (JsonException)
		{
			return false;
		}
	}

	private async ValueTask<AppConfig?> TryRestoreAsync(string path, CancellationToken cancellationToken)
	{
		try
		{
			AppConfig? config = await ReadAsync(path, cancellationToken).ConfigureAwait(false);

			if (config is not null)
			{
				LogRestored(logger, path);
			}

			return config;
		}
		catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
		{
			LogConfigCorrupted(logger, exception, path);
			return null;
		}
	}

	private void PreserveCorruptedFile()
	{
		string target = $"{_configPath}.corrupt-{DateTimeOffset.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}";

		try
		{
			File.Move(_configPath, target, true);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			LogConfigUnreadable(logger, exception, _configPath);
			_isReadOnly = true;
		}
	}

	private void SaveCore()
	{
		if (_config is null || _isReadOnly || _savedVersion == _saveVersion)
		{
			return;
		}

		long version = _saveVersion;
		JsonObject node = SerializeToJsonObject(_config);
		RemoveDefaults(node, DefaultNode);

		// 不写默认值，以后调整默认值对老房间同样生效
		if (node[nameof(AppConfig.Rooms)] is JsonArray rooms)
		{
			foreach (JsonObject room in rooms.OfType<JsonObject>())
			{
				RemoveDefaults(room, DefaultRoomNode);
			}
		}

		CreateConfigDirectory();

		File.Delete(TempPath);

		using (FileStream stream = new(TempPath, CreateTempFileOptions()))
		{
			JsonSerializer.Serialize(stream, node, AppConfigJsonContext.Default.JsonObject);
			stream.Flush(true);
		}

		if (File.Exists(_configPath))
		{
			File.Replace(TempPath, _configPath, BackupPath);
		}
		else
		{
			File.Move(TempPath, _configPath);
		}

		_savedVersion = version;
	}

	private static void RemoveDefaults(JsonObject node, JsonObject defaults)
	{
		foreach ((string key, JsonNode? value) in node.ToArray())
		{
			if (JsonNode.DeepEquals(Normalize(value), Normalize(defaults[key])))
			{
				node.Remove(key);
			}
		}

		return;

		static JsonNode? Normalize(JsonNode? node)
		{
			return node?.GetValueKind() is JsonValueKind.String && string.IsNullOrEmpty(node.GetValue<string>()) ? null : node;
		}
	}

	// 配置里有登录 Cookie，Unix 上只允许当前用户读写
	private void CreateConfigDirectory()
	{
		if (Path.GetDirectoryName(_configPath) is not { Length: > 0 } directory)
		{
			return;
		}

		if (OperatingSystem.IsWindows())
		{
			Directory.CreateDirectory(directory);
		}
		else
		{
			Directory.CreateDirectory(directory, PrivateDirectoryMode);
		}
	}

	private static FileStreamOptions CreateTempFileOptions()
	{
		FileStreamOptions options = new() { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };

		if (!OperatingSystem.IsWindows())
		{
			options.UnixCreateMode = PrivateFileMode;
		}

		return options;
	}

	private void RestrictPermissions()
	{
		if (OperatingSystem.IsWindows())
		{
			return;
		}

		RestrictPermissions(_configPath);
		RestrictPermissions(BackupPath);
	}

	[UnsupportedOSPlatform("windows")]
	private void RestrictPermissions(string path)
	{
		try
		{
			if (File.Exists(path))
			{
				File.SetUnixFileMode(path, PrivateFileMode);
			}
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			LogSaveFailed(logger, exception, path);
		}
	}

	private static JsonObject SerializeToJsonObject(AppConfig config)
	{
		return JsonSerializer.SerializeToNode(config, AppConfigJsonContext.Default.AppConfig) as JsonObject
				?? throw new InvalidOperationException("Serialized app config is not a JSON object.");
	}

	[LoggerMessage(Level = LogLevel.Error, Message = "Config file {Path} is corrupted")]
	private static partial void LogConfigCorrupted(ILogger logger, Exception exception, string path);

	[LoggerMessage(Level = LogLevel.Error, Message = "Config file {Path} cannot be read, changes will not be saved")]
	private static partial void LogConfigUnreadable(ILogger logger, Exception exception, string path);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Config value {Name} in {Path} is invalid and was reset to its default")]
	private static partial void LogInvalidValue(ILogger logger, string name, string path);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Config restored from {Path}")]
	private static partial void LogRestored(ILogger logger, string path);

	[LoggerMessage(Level = LogLevel.Error, Message = "Failed to save config file {Path}")]
	private static partial void LogSaveFailed(ILogger logger, Exception exception, string path);
}
