using BilibiliLiveRecordDownLoader.Interfaces;
using BilibiliLiveRecordDownLoader.Models;
using BilibiliLiveRecordDownLoader.Shared;
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
			Encoder = JavaScriptEncoder.Default
		};

		public ConfigService(ILogger<ConfigService> logger)
		{
			_logger = logger;
			_configMonitor = this.WhenAnyValue(
					x => x.Config,
					x => x.Config.RoomId,
					x => x.Config.MainDir,
					x => x.Config.DownloadThreads,
					x => x.Config.IsCheckUpdateOnStart,
					x => x.Config.IsCheckPreRelease
					)
				.Throttle(TimeSpan.FromSeconds(1))
				.DistinctUntilChanged()
				.Where(v => v.Item1 != null && !_lock.IsWriteLockHeld)
				.Subscribe(_ => SaveAsync(default).NoWarning());
		}

		public async Task SaveAsync(CancellationToken token)
		{
			try
			{
				await using var _ = await _lock.WriteLockAsync(token);

				await using var stream = new MemoryStream();

				await JsonSerializer.SerializeAsync(stream, Config, JsonOptions, token);
				stream.Position = 0;

				await using var fs = new FileStream(FilePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true);

				await stream.CopyToAsync(fs, token);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, @"Save Config Error!");
			}
		}

		public async Task LoadAsync(CancellationToken token)
		{
			try
			{
				await using var _ = await _lock.ReadLockAsync(token);

				await using var fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, true);

				var config = await JsonSerializer.DeserializeAsync<Config>(fs, cancellationToken: token);
				if (config != null)
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
