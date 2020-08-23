using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using BilibiliLiveRecordDownLoader.BilibiliApi;
using BilibiliLiveRecordDownLoader.Http;

namespace BilibiliLiveRecordDownLoader.Services
{
    public class LiveRecordDownloadTask
    {
        private const double ThreadsCount = 32.0;

        private readonly string _id;
        private readonly ConcurrentDictionary<string, LiveRecordDownloadTask> _parent;
        private readonly string _root;
        private string RecordPath => Path.Combine(_root, _id);

        private readonly Subject<double> _progress = new Subject<double>();
        public IObservable<double> ProgressUpdated => _progress.AsObservable();

        private CancellationTokenSource _cts;

        public bool IsDownloading => _cts != null && !_cts.IsCancellationRequested;

        public LiveRecordDownloadTask(string id, ConcurrentDictionary<string, LiveRecordDownloadTask> parent, string path)
        {
            _id = id;
            _parent = parent;
            _root = path;
        }

        /// <summary>
        /// 开始或停止下载
        /// </summary>
        public async Task StartOrStop()
        {
            if (IsDownloading)
            {
                Stop();
            }
            else
            {
                await Start();
            }
        }

        private async Task Start()
        {
            try
            {
                _cts = new CancellationTokenSource();
                var downloader = new Downloader();

                using var client = new BililiveApiClient();
                var message = await client.GetLiveRecordUrl(_id, _cts.Token);

                var list = message?.data?.list;
                if (list != null)
                {
                    var l = list.Where(x => !string.IsNullOrEmpty(x.url) || !string.IsNullOrEmpty(x.backup_url))
                            .Select(x => string.IsNullOrEmpty(x.url) ? x.backup_url : x.url).ToArray();

                    _progress.OnNext(0.0);

                    for (var i = 0; i < l.Length; ++i)
                    {
                        if (_cts.Token.IsCancellationRequested)
                        {
                            throw new TaskCanceledException(@"下载已取消！");
                        }

                        var url = l[i];
                        var outfile = Path.Combine(RecordPath, $@"{i + 1}.flv");
                        if (File.Exists(outfile))
                        {
                            _progress.OnNext((1.0 + i) / l.Length);
                            continue;
                        }

                        await downloader.DownloadFile(url, ThreadsCount, outfile, RecordPath,
                                d => { _progress.OnNext((d + i) / l.Length); }, _cts.Token);
                    }


                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                // _progress.OnError(ex);
            }
            finally
            {
                _progress.OnCompleted();
                _cts?.Dispose();
                _cts = null;
                _parent.TryRemove(_id, out _);
            }
        }

        private void Stop()
        {
            _cts?.Cancel();
        }
    }
}
