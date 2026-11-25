using System.Diagnostics.CodeAnalysis;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization.Metadata;

namespace BilibiliLiveRecordDownLoader.Configuration;

internal sealed partial class AppConfigManager(IOptions<AppConfigStorageOptions> storageOptions, TimeProvider timeProvider, ILogger<AppConfigManager> logger) : IAppConfigManager, IDisposable, IAsyncDisposable
{
	private const int ReadAttempts = 3;
	private const int SaveAttempts = 3;
	private const UnixFileMode PrivateFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
	private const UnixFileMode PrivateDirectoryMode = PrivateFileMode | UnixFileMode.UserExecute;

	private static readonly TimeSpan ReadRetryDelay = TimeSpan.FromMilliseconds(200);
	private static readonly TimeSpan SaveRetryDelay = TimeSpan.FromSeconds(1);

	// 逐项清理用的 JsonObject 遇到重复的键会抛 ArgumentException，解析时就拒绝
	private static readonly JsonDocumentOptions DocumentOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true, AllowDuplicateProperties = false };
	private static readonly JsonObject DefaultNode = SerializeToJsonObject(AppConfig.CreateDefault(), AppConfigJsonContext.Default.AppConfig);
	private static readonly JsonObject DefaultRoomNode = SerializeToJsonObject(new RoomConfig(), AppConfigJsonContext.Default.RoomConfig);

	// 配置允许手改，非 ASCII 字符原样写出；文件不会嵌进网页，不需要 HTML 转义
	private static readonly JsonWriterOptions WriterOptions = new() { Indented = true, IndentCharacter = '\t', IndentSize = 1, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

	private readonly string _configPath = storageOptions.Value.FilePath;
	private readonly string? _legacyPath = storageOptions.Value.LegacyFilePath;
	private readonly TimeSpan _saveDelay = storageOptions.Value.SaveDelay;
	[SuppressMessage("Usage", "CA2213", Justification = "没有需要释放的等待句柄，释放后再次 Dispose 时 Wait 会抛异常")]
	private readonly SemaphoreSlim _lock = new(1, 1);
	[SuppressMessage("Usage", "CA2213", Justification = "没有定时器和链接，不需要释放，释放后再次 Dispose 时 Cancel 会抛异常")]
	private readonly CancellationTokenSource _disposal = new();
	private bool _isDirty;
	private bool _isSaveScheduled;
	private bool _isReadOnly;
	private bool _isCorruptFileInPlace;
	private bool _isMigrated;
	private bool _disposed;
	private AppConfig? _config;

	public bool IsLegacyConfigImported { get; private set; }

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

	public async ValueTask UpdateAsync(Action<AppConfig> update, CancellationToken cancellationToken = default)
	{
		await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);

		try
		{
			await UpdateCoreAsync(update, cancellationToken).ConfigureAwait(false);
			ScheduleSave();
		}
		finally
		{
			_lock.Release();
		}
	}

	public async ValueTask UpdateAndSaveAsync(Action<AppConfig> update, CancellationToken cancellationToken = default)
	{
		await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);

		try
		{
			await UpdateCoreAsync(update, cancellationToken).ConfigureAwait(false);

			// 读不到的配置不会写回，不能当作已经存下
			if (_isReadOnly)
			{
				throw new IOException($"Config file {_configPath} cannot be read, changes will not be saved.");
			}

			try
			{
				WriteCore();
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				ScheduleSave();
				throw;
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
			if (!_disposed)
			{
				SaveCore();
				_disposed = true;
			}
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

	private void ScheduleSave()
	{
		if (!_isSaveScheduled)
		{
			_isSaveScheduled = true;
			_ = SaveAfterDelayAsync(_disposal.Token);
		}
	}

	private async Task SaveAfterDelayAsync(CancellationToken cancellationToken)
	{
		try
		{
			for (int attempt = 1; attempt <= SaveAttempts; ++attempt)
			{
				await Task.Delay(attempt is 1 ? _saveDelay : SaveRetryDelay, timeProvider, cancellationToken).ConfigureAwait(false);
				await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);

				try
				{
					// 先清掉标记：SaveCore 抛异常时之后的修改还能重新排程
					_isSaveScheduled = false;

					if (SaveCore())
					{
						return;
					}

					// 还要重试时仍算已排程：等待期间的修改不另起一轮，下次重试会一并写入
					_isSaveScheduled = attempt < SaveAttempts;
				}
				finally
				{
					_lock.Release();
				}
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception exception)
		{
			LogSaveFailed(logger, exception, _configPath);
		}
	}

	private async ValueTask UpdateCoreAsync(Action<AppConfig> update, CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		AppConfig config = await GetOrLoadCoreAsync(cancellationToken).ConfigureAwait(false);
		update(config);
		config.ApplyDefaults();
		_isDirty = true;
	}

	private async ValueTask<AppConfig> GetOrLoadCoreAsync(CancellationToken cancellationToken)
	{
		if (_config is null)
		{
			(_config, _isDirty) = await LoadCoreAsync(cancellationToken).ConfigureAwait(false);

			if (!SaveCore())
			{
				ScheduleSave();
			}
		}

		return _config;
	}

	private async ValueTask<(AppConfig Config, bool Unsaved)> LoadCoreAsync(CancellationToken cancellationToken)
	{
		AppConfig? config = null;
		bool unsaved = false;

		try
		{
			config = await ReadWithRetryAsync(_configPath, cancellationToken).ConfigureAwait(false);

			// 上次保存在替换文件时中断，新内容还留在临时文件里；只剩备份时视为用户删除配置来重置
			if (config is null && File.Exists(TempPath))
			{
				if (await TryRestoreTempFileAsync(cancellationToken).ConfigureAwait(false) is { } restored)
				{
					config = restored;
					// 临时文件已经完整写好，放回原处就行；重写要先删掉这份唯一的最新内容，写不成就只剩更旧的备份
					unsaved = !TryMoveTempFileBack();
				}
				else
				{
					config = await TryRestoreAsync(BackupPath, cancellationToken).ConfigureAwait(false);
					unsaved = config is not null;
				}
			}

			// 没有备份才算第一次运行；用户删配置文件来重置时备份还在，不导入旧版
			if (config is null && !File.Exists(BackupPath) && await TryImportLegacyAsync(cancellationToken).ConfigureAwait(false) is { } legacy)
			{
				config = legacy;
				unsaved = true;
				IsLegacyConfigImported = true;
			}
		}
		catch (JsonException exception)
		{
			config = await TryReadSkippingInvalidValuesAsync(cancellationToken).ConfigureAwait(false);

			if (config is null)
			{
				LogConfigCorrupted(logger, exception, _configPath);
				config = await TryRestoreAsync(BackupPath, cancellationToken).ConfigureAwait(false);
				PreserveCorruptedFile(config is not null);
			}

			unsaved = config is not null;
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

		// 迁移之后立即写回，已删掉的设置不再留在文件里
		return (config, unsaved || _isMigrated);
	}

	private async ValueTask<AppConfig?> ReadWithRetryAsync(string path, CancellationToken cancellationToken)
	{
		for (int attempt = 1; ; ++attempt)
		{
			try
			{
				return await ReadAsync(path, cancellationToken).ConfigureAwait(false);
			}
			catch (IOException) when (attempt < ReadAttempts)
			{
				await Task.Delay(ReadRetryDelay, timeProvider, cancellationToken).ConfigureAwait(false);
			}
		}
	}

	private async ValueTask<AppConfig?> ReadAsync(string path, CancellationToken cancellationToken)
	{
		if (!File.Exists(path))
		{
			return null;
		}

		await using FileStream stream = File.OpenRead(path);
		// 顶层的 null 反序列化不报错；不按损坏处理就会当成没有配置，下次保存还会拿它替换掉好的备份
		AppConfig config = await JsonSerializer.DeserializeAsync(stream, AppConfigJsonContext.Default.AppConfig, cancellationToken).ConfigureAwait(false)
			?? throw new JsonException("The config is not a JSON object.");

		stream.Position = 0;
		Migrate(config, () => JsonSerializer.Deserialize(stream, AppConfigJsonContext.Default.RetiredSettings));
		return config;
	}

	// 已经删掉的设置读不出来时当作没写，不影响其他设置
	private void Migrate(AppConfig config, Func<RetiredSettings?> read)
	{
		RetiredSettings? retired;

		try
		{
			retired = read();
		}
		catch (JsonException)
		{
			return;
		}

		_isMigrated |= retired?.ApplyTo(config) is true;
	}

	private async ValueTask<AppConfig?> TryReadSkippingInvalidValuesAsync(CancellationToken cancellationToken)
	{
		try
		{
			return await ReadObjectAsync(_configPath, cancellationToken).ConfigureAwait(false) is { } root ? DeserializeSkippingInvalidValues(root, _configPath) : null;
		}
		catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
		{
			return null;
		}
	}

	private async ValueTask<AppConfig?> TryImportLegacyAsync(CancellationToken cancellationToken)
	{
		if (_legacyPath is null)
		{
			return null;
		}

		foreach (string path in (string[])[_legacyPath, LegacyConfig.GetBackupPath(_legacyPath)])
		{
			if (await TryReadLegacyAsync(path, cancellationToken).ConfigureAwait(false) is { } config)
			{
				LogLegacyImported(logger, path);
				return config;
			}
		}

		return null;
	}

	// 旧版的文件只读不改，退回旧版时还能接着用
	private async ValueTask<AppConfig?> TryReadLegacyAsync(string path, CancellationToken cancellationToken)
	{
		try
		{
			if (!File.Exists(path))
			{
				return null;
			}

			JsonObject legacy = await ReadObjectAsync(path, cancellationToken).ConfigureAwait(false) ?? throw new JsonException("The legacy config is not a JSON object.");
			return DeserializeSkippingInvalidValues(LegacyConfig.ToCurrent(legacy), path);
		}
		catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
		{
			LogLegacyUnreadable(logger, exception, path);
			return null;
		}
	}

	private static async ValueTask<JsonObject?> ReadObjectAsync(string path, CancellationToken cancellationToken)
	{
		await using FileStream stream = File.OpenRead(path);
		return await JsonNode.ParseAsync(stream, documentOptions: DocumentOptions, cancellationToken: cancellationToken).ConfigureAwait(false) as JsonObject;
	}

	private AppConfig? DeserializeSkippingInvalidValues(JsonObject root, string path)
	{
		RemoveInvalidValues(root, AppConfigJsonContext.Default.AppConfig, string.Empty, path);
		AppConfig? config = root.Deserialize(AppConfigJsonContext.Default.AppConfig);

		if (config is not null)
		{
			Migrate(config, () => root.Deserialize(AppConfigJsonContext.Default.RetiredSettings));
		}

		return config;
	}

	private void RemoveInvalidValues(JsonObject node, JsonTypeInfo typeInfo, string prefix, string path)
	{
		foreach ((string key, JsonNode? value) in node.ToArray())
		{
			if (typeInfo.Properties.FirstOrDefault(candidate => string.Equals(candidate.Name, key, StringComparison.OrdinalIgnoreCase)) is not { } property
				|| AppConfigJsonContext.Default.GetTypeInfo(property.PropertyType) is not { } propertyTypeInfo)
			{
				continue;
			}

			string name = prefix + property.Name;

			// 列表里读不出的项只丢这一项，保留其余项和用户排好的顺序
			if (value is JsonArray items && propertyTypeInfo is { Kind: JsonTypeInfoKind.Enumerable, ElementType: { } elementType } && AppConfigJsonContext.Default.GetTypeInfo(elementType) is { } elementTypeInfo)
			{
				for (int i = items.Count - 1; i >= 0; i--)
				{
					if (items[i] is JsonObject item && elementTypeInfo.Kind is JsonTypeInfoKind.Object)
					{
						RemoveInvalidValues(item, elementTypeInfo, $"{name}[{i}].", path);
					}
					else if (items[i] is null || !CanRead(items[i], elementTypeInfo))
					{
						LogInvalidValue(logger, $"{name}[{i}]", path);
						items.RemoveAt(i);
					}
				}
			}

			if (!CanRead(value, propertyTypeInfo))
			{
				LogInvalidValue(logger, name, path);
				node.Remove(key);
			}
		}
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

	// 只有内容损坏才退回备份：暂时读不到时退回备份再写盘会删掉这份唯一的最新内容
	private async ValueTask<AppConfig?> TryRestoreTempFileAsync(CancellationToken cancellationToken)
	{
		try
		{
			AppConfig? config = await ReadWithRetryAsync(TempPath, cancellationToken).ConfigureAwait(false);

			if (config is not null)
			{
				LogRestored(logger, TempPath);
			}

			return config;
		}
		catch (JsonException exception)
		{
			LogConfigCorrupted(logger, exception, TempPath);
			return null;
		}
	}

	private bool TryMoveTempFileBack()
	{
		try
		{
			File.Move(TempPath, _configPath);
			return true;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			LogSaveFailed(logger, exception, _configPath);
			return false;
		}
	}

	// 恢复出配置时只复制：取消或写回失败时下次照样从备份恢复，移走的话只剩备份会被当成用户删了配置
	// 没有可恢复的就移走，否则每次启动都会再当成损坏处理一遍
	private void PreserveCorruptedFile(bool isRestored)
	{
		string target = $"{_configPath}.corrupt-{timeProvider.GetLocalNow().ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}";

		try
		{
			if (isRestored)
			{
				File.Copy(_configPath, target, true);
				_isCorruptFileInPlace = true;
			}
			else
			{
				File.Move(_configPath, target, true);
			}
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			LogConfigUnreadable(logger, exception, _configPath);
			_isReadOnly = true;
			return;
		}

		// 损坏的文件可能是旧版本写的，权限没收紧过，里面同样有 Cookie
		if (!OperatingSystem.IsWindows())
		{
			RestrictPermissions(target);
		}
	}

	private bool SaveCore()
	{
		try
		{
			WriteCore();
			return true;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			LogSaveFailed(logger, exception, _configPath);
			return false;
		}
	}

	// Dispose 之后才第一次加载时，恢复或导入的内容不再写，下次启动会重新来过
	private void WriteCore()
	{
		if (_config is null || _isReadOnly || !_isDirty || _disposed)
		{
			return;
		}

		// 不写默认值，以后调整默认值对老房间同样生效
		JsonObject node = SerializeToJsonObject(_config, AppConfigJsonContext.Default.AppConfig);
		RemoveDefaults(node, DefaultNode);

		if (node[nameof(AppConfig.Rooms)] is JsonArray rooms)
		{
			foreach (JsonObject room in rooms.OfType<JsonObject>())
			{
				RemoveDefaults(room, DefaultRoomNode);
			}
		}

		CreateConfigDirectory();
		File.Delete(TempPath);
		WriteTempFile(node);

		if (File.Exists(_configPath))
		{
			// 原处还是损坏的文件时不拿它替换备份
			File.Replace(TempPath, _configPath, _isCorruptFileInPlace ? null : BackupPath);
		}
		else
		{
			File.Move(TempPath, _configPath);
		}

		_isDirty = false;
		_isCorruptFileInPlace = false;
	}

	private void WriteTempFile(JsonObject node)
	{
		using FileStream stream = new(TempPath, CreateTempFileOptions());
		using Utf8JsonWriter writer = new(stream, WriterOptions);
		node.WriteTo(writer);
		writer.Flush();
		stream.Flush(true);
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
			LogPermissionsNotRestricted(logger, exception, path);
		}
	}

	private static JsonObject SerializeToJsonObject<T>(T value, JsonTypeInfo<T> typeInfo)
	{
		return JsonSerializer.SerializeToNode(value, typeInfo) as JsonObject ?? throw new InvalidOperationException($"Serialized {typeof(T).Name} is not a JSON object.");
	}

	[LoggerMessage(Level = LogLevel.Error, Message = "Config file {Path} is corrupted")]
	private static partial void LogConfigCorrupted(ILogger logger, Exception exception, string path);

	[LoggerMessage(Level = LogLevel.Error, Message = "Config file {Path} cannot be read, changes will not be saved")]
	private static partial void LogConfigUnreadable(ILogger logger, Exception exception, string path);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Config value {Name} in {Path} is invalid and was reset to its default")]
	private static partial void LogInvalidValue(ILogger logger, string name, string path);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Config restored from {Path}")]
	private static partial void LogRestored(ILogger logger, string path);

	[LoggerMessage(Level = LogLevel.Information, Message = "Imported the legacy config from {Path}")]
	private static partial void LogLegacyImported(ILogger logger, string path);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Legacy config file {Path} cannot be imported")]
	private static partial void LogLegacyUnreadable(ILogger logger, Exception exception, string path);

	[LoggerMessage(Level = LogLevel.Error, Message = "Failed to save config file {Path}")]
	private static partial void LogSaveFailed(ILogger logger, Exception exception, string path);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Cannot restrict the permissions of {Path}")]
	private static partial void LogPermissionsNotRestricted(ILogger logger, Exception exception, string path);
}
