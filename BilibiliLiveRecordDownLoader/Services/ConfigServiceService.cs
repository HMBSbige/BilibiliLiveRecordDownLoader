using BilibiliLiveRecordDownLoader.Interfaces;
using BilibiliLiveRecordDownLoader.Models;
using BilibiliLiveRecordDownLoader.Utils;
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
    [Serializable]
    public class ConfigServiceService : ReactiveObject, IConfigService
    {
        private Config _config;

        public Config Config
        {
            get => _config;
            private set => this.RaiseAndSetIfChanged(ref _config, value);
        }

        public string FilePath { get; set; } = $@"{nameof(BilibiliLiveRecordDownLoader)}.json";

        private readonly ILogger _logger;

        private IDisposable _configMonitor;

        private readonly AsyncReaderWriterLock _lock = new AsyncReaderWriterLock();

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.Default
        };

        public ConfigServiceService(ILogger<ConfigServiceService> logger)
        {
            _logger = logger;
            _configMonitor = this.WhenAnyValue(x => x.Config,
                x => x.Config.RoomId,
                x => x.Config.MainDir,
                x => x.Config.DownloadThreads)
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

                Config = await JsonSerializer.DeserializeAsync<Config>(fs, cancellationToken: token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, @"Load Config Error!");
            }
            finally
            {
                if (Config == null)
                {
                    Init();
                }
            }
        }

        private void Init()
        {
            Config = new Config
            {
                RoomId = 732,
                MainDir = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                DownloadThreads = 8
            };
        }

        public void Dispose()
        {
            _configMonitor?.Dispose();
        }
    }
}
