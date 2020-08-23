using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using BilibiliLiveRecordDownLoader.BilibiliApi;
using BilibiliLiveRecordDownLoader.FlvProcessor;
using BilibiliLiveRecordDownLoader.Http;

namespace BilibiliLiveRecordDownLoader.Services
{
    public class LiveRecordDownloadTask
    {
        private const double ThreadsCount = 32.0;

        private readonly string _id;
        private readonly DateTime _startTime;
        private readonly DownloadTaskPool _parent;
        private readonly string _root;
        private string RecordPath => Path.Combine(_root, _id);

        private readonly Subject<double> _progress = new Subject<double>();
        public IObservable<double> ProgressUpdated => _progress.AsObservable();

        private CancellationTokenSource _cts;

        public bool IsDownloading => _cts != null && !_cts.IsCancellationRequested;

        public LiveRecordDownloadTask(string id, DateTime startTime, DownloadTaskPool parent, string path)
        {
            _id = id;
            _startTime = startTime;
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

                    //Merge flv
                    _progress.OnNext(0.99);

                    var filename = _startTime == default ? _id : $@"{_startTime:yyyyMMdd_HHmmss}";
                    var mergeFlv = Path.Combine(_root, $@"{filename}.flv");
                    if (l.Length > 1)
                    {
                        var flv = new FlvMerger();
                        flv.AddRange(Enumerable.Range(1, l.Length).Select(i => Path.Combine(RecordPath, $@"{i}.flv")));

                        flv.Merge(mergeFlv);
                        Utils.Utils.DeleteFiles(RecordPath);
                    }
                    else if (l.Length == 1)
                    {
                        var inputFile = Path.Combine(RecordPath, @"1.flv");
                        File.Move(inputFile, mergeFlv, true);
                        Utils.Utils.DeleteFiles(RecordPath);
                    }

                    _progress.OnNext(100.0);
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
                _parent.Remove(_id);
            }
        }

        private void Stop()
        {
            _cts?.Cancel();
        }
    }
}
