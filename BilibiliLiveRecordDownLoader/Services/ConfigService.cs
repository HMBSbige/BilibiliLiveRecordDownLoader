using BilibiliApi.Clients;
using BilibiliLiveRecordDownLoader.Interfaces;
using BilibiliLiveRecordDownLoader.Models;
using BilibiliLiveRecordDownLoader.Shared.Utils;
using DynamicData;
using DynamicData.Binding;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Threading;
using ReactiveUI;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reactive.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BilibiliLiveRecordDownLoader.Services
{
	public sealed class ConfigService : ReactiveObject, IConfigService
	{
		public Config Config { get; }

		public string FilePath { get; set; } = $@"{nameof(BilibiliLiveRecordDownLoader)}.json";

		private readonly ILogger _logger;

		private readonly IDisposable _configMonitor;
		private readonly IDisposable _networkSettingMonitor;
		private IDisposable? _roomsMonitor;

		private readonly AsyncReaderWriterLock _lock = new();

		private static readonly JsonSerializerOptions JsonOptions = new()
		{
			WriteIndented = true,
			Encoder = JavaScriptEncoder.Default,
			IgnoreReadOnlyProperties = true,
		};

		private static readonly string[] RoomProperties = Utils.Utils.GetPropertiesNameExcludeJsonIgnore(typeof(RoomStatus)).ToArray();

		public ConfigService(
			ILogger<ConfigService> logger,
			Config config,
			BililiveApiClient apiClient)
		{
			_logger = logger;
			Config = config;

			_configMonitor = Config.WhenAnyPropertyChanged()
				.Throttle(TimeSpan.FromSeconds(1))
				.Where(_ => !_lock.IsWriteLockHeld)
				.Subscribe(_ =>
				{
					SaveAsync(default).NoWarning();

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
						var (cookie, ua, useProxy) = x;
						var handler = new SocketsHttpHandler
						{
							PooledConnectionLifetime = TimeSpan.FromMinutes(10),
							UseCookies = string.IsNullOrWhiteSpace(cookie),
							UseProxy = useProxy
						};
						Config.HttpHandler = handler;
						apiClient.Client = HttpClientUtils.BuildClientForBilibili(ua, cookie, handler);
					});
		}

		public async ValueTask SaveAsync(CancellationToken token)
		{
			try
			{
				await using var _ = await _lock.WriteLockAsync(token);

				await using var fs = new FileStream(FilePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true);

				await JsonSerializer.SerializeAsync(fs, Config, JsonOptions, token);
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
				await using var _ = await _lock.ReadLockAsync(token);

				await using var fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, true);

				var config = await JsonSerializer.DeserializeAsync<Config>(fs, cancellationToken: token);
				if (config is not null)
				{
					Config.Clone(config);
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, @"Load Config Error!");
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
			_roomsMonitor?.Dispose();
		}
	}
}
