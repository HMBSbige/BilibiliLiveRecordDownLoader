using BilibiliApi.Clients;
using BilibiliLiveRecordDownLoader.Interfaces;
using BilibiliLiveRecordDownLoader.Models;
using BilibiliLiveRecordDownLoader.Shared.Utils;
using BilibiliLiveRecordDownLoader.Utils;
using DynamicData;
using DynamicData.Binding;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Threading;
using ModernWpf;
using ReactiveUI;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reactive.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace BilibiliLiveRecordDownLoader.Services;

public sealed class ConfigService : ReactiveObject, IConfigService
{
	public Config Config { get; }

	public string FilePath => nameof(BilibiliLiveRecordDownLoader) + @".json";

	public string BackupFilePath => nameof(BilibiliLiveRecordDownLoader) + @".backup.json";

	private readonly ILogger _logger;

	private readonly IDisposable _configMonitor;
	private readonly IDisposable _networkSettingMonitor;
	private readonly IDisposable _themeMonitor;
	private IDisposable? _roomsMonitor;

	private readonly AsyncReaderWriterLock _lock = new(null);

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		WriteIndented = true,
		Encoder = JavaScriptEncoder.Default,
		IgnoreReadOnlyProperties = true,
	};

	private static readonly string[] RoomProperties = typeof(RoomStatus).GetPropertiesExcludeJsonIgnore().Select(p => p.Name).ToArray();

	public ConfigService(
		ILogger<ConfigService> logger,
		Config config,
		BilibiliApiClient apiClient)
	{
		_logger = logger;
		Config = config;

		_configMonitor = Config.WhenAnyPropertyChanged()
			.Throttle(TimeSpan.FromSeconds(1))
			.Where(_ => !_lock.IsWriteLockHeld)
			.Subscribe(_ =>
			{
				SaveAsync(default).Forget();

				// 监控 房间设置 变化
				_roomsMonitor?.Dispose();
				_roomsMonitor = Config.Rooms.AsObservableChangeSet()
					.WhenAnyPropertyChanged(RoomProperties)
					.Subscribe(_ => RaiseRoomsChanged());
			});

		_networkSettingMonitor = Config.WhenAnyValue(x => x.Cookie, x => x.UserAgent, x => x.IsUseProxy)
			.Throttle(TimeSpan.FromSeconds(0.5))
			.DistinctUntilChanged()
			.Subscribe(x =>
			{
				(string cookie, string ua, bool useProxy) = x;

				SocketsHttpHandler handler = new()
				{
					UseCookies = string.IsNullOrWhiteSpace(cookie),
					UseProxy = useProxy
				};
				Config.HttpHandler = handler;

				apiClient.Client = HttpClientUtils.BuildClientForBilibili(ua, cookie, handler);
				WebRequest.DefaultWebProxy = useProxy ? WebRequest.GetSystemWebProxy() : null;
			});

		_themeMonitor = Config.WhenAnyValue(x => x.Theme)
			.DistinctUntilChanged()
			.Subscribe(ThemeManager.Current.SetTheme);
	}

	public async ValueTask SaveAsync(CancellationToken token)
	{
		try
		{
			await using var _ = await _lock.WriteLockAsync(token);

			string tempFile = Path.ChangeExtension(Path.GetFileNameWithoutExtension(Path.GetTempFileName()), Path.GetExtension(FilePath));

			await using (FileStream fs = new(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true))
			{
				await JsonSerializer.SerializeAsync(fs, Config, JsonOptions, token);
			}

			await EnsureConfigFileExistsAsync();

			File.Replace(tempFile, FilePath, BackupFilePath);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, @"Save Config Error!");
		}
	}

	public async ValueTask LoadAsync(CancellationToken token)
	{
		try
		{
			if (!File.Exists(FilePath))
			{
				await SaveAsync(token);
				return;
			}

			await using var _ = await _lock.ReadLockAsync(token);

			if (await LoadAsync(FilePath, token))
			{
				return;
			}

			_logger.LogInformation(@"尝试加载备份文件 {file}", BackupFilePath);
			await LoadAsync(BackupFilePath, token);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, @"Load Config Error!");
		}
	}

	private async ValueTask<bool> LoadAsync(string filename, CancellationToken token)
	{
		try
		{
			await using FileStream fs = new(filename, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);

			Config? config = await JsonSerializer.DeserializeAsync<Config>(fs, cancellationToken: token);
			if (config is not null)
			{
				Config.Clone(config);
			}

			return true;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, @"Load {file} Error!", filename);

			return false;
		}
	}

	private async ValueTask EnsureConfigFileExistsAsync()
	{
		if (!File.Exists(FilePath))
		{
			await File.Create(FilePath).DisposeAsync();
		}
	}

	private void RaiseRoomsChanged()
	{
		Config.RaisePropertyChanged(nameof(Config.Rooms));
	}

	public void Dispose()
	{
		_configMonitor.Dispose();
		_networkSettingMonitor.Dispose();
		_themeMonitor.Dispose();
		_roomsMonitor?.Dispose();
	}
}
