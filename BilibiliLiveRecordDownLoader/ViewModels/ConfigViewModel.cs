using System;
using System.Diagnostics;
using System.IO;
using System.Reactive.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BilibiliLiveRecordDownLoader.Models;
using ReactiveUI;

namespace BilibiliLiveRecordDownLoader.ViewModels
{
    [Serializable]
    public class ConfigViewModel : ReactiveObject
    {
        #region 字段

        private long _roomId;
        private string _mainDir;

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

        #endregion

        private static readonly ReaderWriterLockSlim Lock = new ReaderWriterLockSlim();

        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.Default
        };

        private static string _filename = $@"{nameof(BilibiliLiveRecordDownLoader)}.json";

        private string _path;

        public ConfigViewModel(string path)
        {
            _roomId = 732;
            MainDir = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            _path = Path.Combine(Utils.Utils.EnsureDir(path), _filename);

            this.WhenAnyValue(x => x.RoomId)
                    .Throttle(TimeSpan.FromSeconds(1))
                    .DistinctUntilChanged()
                    .Subscribe(async _ => { await SaveAsync(); });

            this.WhenAnyValue(x => x.MainDir)
                    .Throttle(TimeSpan.FromSeconds(1))
                    .DistinctUntilChanged()
                    .Subscribe(async _ => { await SaveAsync(); });
        }

        public async Task SaveAsync(CancellationToken token = default)
        {
            Lock.EnterWriteLock();
            try
            {
                await using var stream = new MemoryStream();

                var config = new Config();
                CopyTo(config);

                await JsonSerializer.SerializeAsync(stream, config, config.GetType(), Options, token);
                stream.Position = 0;
                using var reader = new StreamReader(stream);
                var str = await reader.ReadToEndAsync();
                await File.WriteAllTextAsync(_path, str, Encoding.UTF8, token);
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
                await using var fs = File.OpenRead(_path);
                var config = await JsonSerializer.DeserializeAsync<Config>(fs, Options, token);
                CopyFrom(config);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }

        public void CopyFrom(Config config)
        {
            RoomId = config.RoomId;
            MainDir = config.MainDir;
        }

        public void CopyTo(Config config)
        {
            config.RoomId = RoomId;
            config.MainDir = MainDir;
        }
    }
}
