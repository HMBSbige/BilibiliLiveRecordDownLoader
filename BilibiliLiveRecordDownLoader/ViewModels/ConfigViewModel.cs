using System;
using System.Diagnostics;
using System.IO;
using System.Reactive.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BilibiliLiveRecordDownLoader.Models;
using ReactiveUI;

namespace BilibiliLiveRecordDownLoader.ViewModels
{
    [Serializable]
    public class ConfigViewModel : ReactiveObject, IDisposable
    {
        #region 字段

        private long _roomId;
        private string _mainDir;
        private byte _downloadThreads;

        #endregion

        #region 属性

        public long RoomId
        {
            get => _roomId;
            set => this.RaiseAndSetIfChanged(ref _roomId, value);
        }

        public string MainDir
        {
            get => _mainDir;
            set => this.RaiseAndSetIfChanged(ref _mainDir, value);
        }

        public byte DownloadThreads
        {
            get => Math.Max((byte)1, _downloadThreads);
            set => this.RaiseAndSetIfChanged(ref _downloadThreads, value);
        }

        #endregion

        private IDisposable _configMonitor;

        private static readonly ReaderWriterLockSlim Lock = new ReaderWriterLockSlim();

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.Default
        };

        private static string _filename = $@"{nameof(BilibiliLiveRecordDownLoader)}.json";

        private string _path;

        public ConfigViewModel(string path)
        {
            _path = Path.Combine(Utils.Utils.EnsureDir(path), _filename);

            _configMonitor = this.WhenAnyValue(x => x.RoomId, x => x.MainDir, x => x.DownloadThreads)
                    .Throttle(TimeSpan.FromSeconds(1))
                    .DistinctUntilChanged()
                    .Where(_ => !Lock.IsWriteLockHeld)
                    .Subscribe(async _ => { await SaveAsync(); });
        }

        public async Task SaveAsync(CancellationToken token = default)
        {
            try
            {
                Lock.EnterWriteLock();
                await using var stream = new MemoryStream();

                var config = new Config();
                CopyTo(config);

                await JsonSerializer.SerializeAsync(stream, config, JsonOptions, token);

                stream.Position = 0;

                await using var fs = new FileStream(_path, FileMode.Create, FileAccess.Write,
                FileShare.None, 4096, true);

                await stream.CopyToAsync(fs, token);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
            finally
            {
                Lock.ExitWriteLock();
            }
        }

        public async Task LoadAsync(CancellationToken token = default)
        {
            try
            {
                Lock.EnterReadLock();

                await using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 4096, true);

                var config = await JsonSerializer.DeserializeAsync<Config>(fs, cancellationToken: token);

                CopyFrom(config);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                RoomId = 732;
                MainDir = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
                DownloadThreads = 8;
            }
            finally
            {
                Lock.ExitReadLock();
            }
        }

        public void CopyFrom(Config config)
        {
            RoomId = config.RoomId;
            MainDir = config.MainDir;
            DownloadThreads = config.DownloadThreads;
        }

        public void CopyTo(Config config)
        {
            config.RoomId = RoomId;
            config.MainDir = MainDir;
            config.DownloadThreads = DownloadThreads;
        }

        public void Dispose()
        {
            _configMonitor?.Dispose();
        }
    }
}
