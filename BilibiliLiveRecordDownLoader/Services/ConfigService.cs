using BilibiliLiveRecordDownLoader.Interfaces;
using BilibiliLiveRecordDownLoader.Models;
using BilibiliLiveRecordDownLoader.Shared.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Threading;
using ReactiveUI;
using System;
using System.IO;
using System.Reactive.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BilibiliLiveRecordDownLoader.Services
{
	public sealed class ConfigService : ReactiveObject, IConfigService
	{
		private Config _config = new();

		public Config Config
		{
			get => _config;
			private set => this.RaiseAndSetIfChanged(ref _config, value);
		}

		public string FilePath { get; set; } = $@"{nameof(BilibiliLiveRecordDownLoader)}.json";

		private readonly ILogger _logger;

		private readonly IDisposable _configMonitor;

		private readonly AsyncReaderWriterLock _lock = new();

		private static readonly JsonSerializerOptions JsonOptions = new()
		{
			WriteIndented = true,
			Encoder = JavaScriptEncoder.Default,
			IgnoreReadOnlyProperties = true,
		};

		public ConfigService(ILogger<ConfigService> logger)
		{
			_logger = logger;
			var c0 = this.WhenAnyValue(
					x => x.Config,
					x => x.Config.RoomId,
					x => x.Config.MainDir,
					x => x.Config.DownloadThreads,
					x => x.Config.IsCheckUpdateOnStart,
					x => x.Config.IsCheckPreRelease
			);
			var c1 = this.WhenAnyValue(
					x => x.Config.MainWindowsWidth,
					x => x.Config.MainWindowsHeight
			);
			_configMonitor = c0.CombineLatest(c1)
				.Throttle(TimeSpan.FromSeconds(1))
				.DistinctUntilChanged()
				.Where(v => v.First.Item1 is not null && !_lock.IsWriteLockHeld)
				.Subscribe(_ => SaveAsync(default).NoWarning());
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
					Config = config;
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, @"Load Config Error!");
			}
		}

		public void Dispose()
		{
			_configMonitor.Dispose();
		}
	}
}
