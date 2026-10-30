namespace BilibiliLiveRecordDownLoader.Configuration;

public sealed partial class AppConfigManager(IOptions<AppConfigStorageOptions> storageOptions, ILogger<AppConfigManager> logger) : IAppConfigManager, IDisposable, IAsyncDisposable
{
	private const int ReadAttempts = 3;
	private const UnixFileMode PrivateFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
	private const UnixFileMode PrivateDirectoryMode = PrivateFileMode | UnixFileMode.UserExecute;

	private static readonly TimeSpan ReadRetryDelay = TimeSpan.FromMilliseconds(200);
	private static readonly JsonObject DefaultNode = SerializeToJsonObject(AppConfig.CreateDefault());

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

	// 修改立即生效；写盘由管理器延迟合并，调用方取消不会丢掉已做的修改
	public async ValueTask UpdateAsync(Action<AppConfig> update, CancellationToken cancellationToken = default)
	{
		await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);

		try
		{
			ObjectDisposedException.ThrowIf(_disposed, this);
			update(await GetOrLoadCoreAsync(cancellationToken).ConfigureAwait(false));
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
		}
		catch (JsonException exception)
		{
			LogConfigCorrupted(logger, exception, _configPath);
			PreserveCorruptedFile();
			config = await TryReadBackupAsync(cancellationToken).ConfigureAwait(false);
			restored = config is not null;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			// 读不到时不能用默认值覆盖用户的配置，本次运行不再写盘
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

	private async ValueTask<AppConfig?> TryReadBackupAsync(CancellationToken cancellationToken)
	{
		try
		{
			AppConfig? backup = await ReadAsync(BackupPath, cancellationToken).ConfigureAwait(false);

			if (backup is not null)
			{
				LogRestoredFromBackup(logger, BackupPath);
			}

			return backup;
		}
		catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
		{
			LogConfigCorrupted(logger, exception, BackupPath);
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

		foreach ((string key, JsonNode? value) in node.ToArray())
		{
			if (JsonNode.DeepEquals(Normalize(value), Normalize(DefaultNode[key])))
			{
				node.Remove(key);
			}
		}

		CreateConfigDirectory();

		string tempPath = _configPath + ".tmp";
		File.Delete(tempPath);

		using (FileStream stream = new(tempPath, CreateTempFileOptions()))
		{
			JsonSerializer.Serialize(stream, node, AppConfigJsonContext.Default.JsonObject);
			stream.Flush(true);
		}

		if (File.Exists(_configPath))
		{
			File.Replace(tempPath, _configPath, BackupPath);
		}
		else
		{
			File.Move(tempPath, _configPath);
		}

		_savedVersion = version;

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

	[LoggerMessage(Level = LogLevel.Warning, Message = "Config restored from {Path}")]
	private static partial void LogRestoredFromBackup(ILogger logger, string path);

	[LoggerMessage(Level = LogLevel.Error, Message = "Failed to save config file {Path}")]
	private static partial void LogSaveFailed(ILogger logger, Exception exception, string path);
}
