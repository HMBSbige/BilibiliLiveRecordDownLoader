using BilibiliLiveRecordDownLoader.Http.HttpPolicy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using Punchclock;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks;

namespace BilibiliLiveRecordDownLoader.Http.DownLoaders
{
    public class MultiThreadedDownloader : IDownloader
    {
        private readonly ILogger _logger;

        private long _fileSize;
        private long _current;
        private long _last;

        private readonly BehaviorSubject<double> _progressUpdated = new BehaviorSubject<double>(0.0);
        public IObservable<double> ProgressUpdated => _progressUpdated.AsObservable();

        private readonly BehaviorSubject<double> _currentSpeed = new BehaviorSubject<double>(0);
        public IObservable<double> CurrentSpeed => _currentSpeed.AsObservable();

        public string UserAgent { get; set; } = @"Mozilla/5.0 (Windows NT 6.1; Trident/7.0; rv:11.0) like Gecko";

        public string Cookie { get; set; }

        public Uri Target { get; set; }

        public ushort Threads { get; set; } = 8;

        public string TempDir { get; set; } = Path.GetTempPath();

        public string OutFileName { get; set; }

        private readonly ObjectPool<HttpClient> _httpClientPool;

        static MultiThreadedDownloader()
        {
            const int connectionLimit = 10000;
            if (ServicePointManager.DefaultConnectionLimit < connectionLimit)
            {
                ServicePointManager.DefaultConnectionLimit = connectionLimit;
            }
        }

        public MultiThreadedDownloader(ILogger<MultiThreadedDownloader> logger)
        {
            _logger = logger;

            var policy = new PooledHttpClientPolicy(CreateNewClient);
            var provider = new DefaultObjectPoolProvider { MaximumRetained = 10 };
            _httpClientPool = provider.Create(policy);
        }

        private HttpClient CreateNewClient()
        {
            var httpHandler = new SocketsHttpHandler();
            if (!string.IsNullOrEmpty(Cookie))
            {
                httpHandler.UseCookies = false;
            }

            var client = new HttpClient(new RetryHandler(httpHandler, 10), true);

            if (!string.IsNullOrEmpty(Cookie))
            {
                client.DefaultRequestHeaders.Add(@"Cookie", Cookie);
            }

            client.DefaultRequestHeaders.Add(@"User-Agent", UserAgent);
            client.DefaultRequestHeaders.ConnectionClose = false;
            client.Timeout = Timeout.InfiniteTimeSpan;

            return client;
        }

        /// <summary>
        /// 获取 Target 的文件大小
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        private async ValueTask<long> GetContentLengthAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            var client = _httpClientPool.Get();
            try
            {
                client.DefaultRequestHeaders.Add(@"User-Agent", UserAgent);

                var result = await client.GetAsync(Target, HttpCompletionOption.ResponseHeadersRead, token);

                var str = result.Content.Headers.First(h => h.Key.Equals(@"Content-Length")).Value.First();
                return long.Parse(str);
            }
            finally
            {
                _httpClientPool.Return(client);
            }
        }

        /// <summary>
        /// 开始下载，若获取大小失败，则会抛出异常
        /// </summary>
        public async ValueTask DownloadAsync(CancellationToken token)
        {
            _fileSize = await GetContentLengthAsync(token); //总大小

            TempDir = EnsureDirectory(TempDir);
            var list = GetFileRangeList();

            var opQueue = new OperationQueue(1);
            _current = 0;
            _last = 0;
            try
            {
                var sw = Stopwatch.StartNew();
                using var monitorSpeed = Observable.Interval(TimeSpan.FromSeconds(1)).Subscribe(_ =>
                {
                    var last = Interlocked.Read(ref _last);
                    _currentSpeed.OnNext(last / sw.Elapsed.TotalSeconds);
                    sw.Restart();
                    Interlocked.Add(ref _last, -last);
                });
                await list.Select(info =>
                {
                    // ReSharper disable once AccessToDisposedClosure
                    return opQueue.Enqueue(1, () => GetStreamAsync(info, token))
                            .ToObservable()
                            .SelectMany(res => WriteToFileAsync(res.Item1, res.Item2, token));
                }).Merge();

                _current = 0;
                await MergeFilesAsync(list, token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation($@"下载已取消：{Target}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, @"下载出错");
#pragma warning disable 4014
                // ReSharper disable once MethodSupportsCancellation
                Task.Run(async () =>
#pragma warning restore 4014
                {
                    foreach (var range in list)
                    {
                        await DeleteFileWithRetryAsync(range.FileName, 3);
                    }
                });
            }
            finally
            {
                await opQueue.ShutdownQueue();
                opQueue.Dispose();
            }
        }

        private static string EnsureDirectory(string path)
        {
            try
            {
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
                return path;
            }
            catch
            {
                return Directory.GetCurrentDirectory();
            }
        }

        private string GetTempFileName() => Path.Combine(TempDir, Path.GetRandomFileName());

        private List<FileRange> GetFileRangeList()
        {
            var list = new List<FileRange>();

            var parts = Threads; //线程数
            var partSize = _fileSize / parts; //每块大小

            _logger.LogDebug($@"总大小：{_fileSize} ({Target})");
            _logger.LogDebug($@"每块大小：{partSize} ({Target})");

            for (var i = 1; i < parts; ++i)
            {
                var range = new RangeHeaderValue((i - 1) * partSize, i * partSize - 1);
                list.Add(new FileRange { FileName = GetTempFileName(), Range = range });
            }

            var last = new RangeHeaderValue((parts - 1) * partSize, _fileSize);
            list.Add(new FileRange { FileName = GetTempFileName(), Range = last });

            return list;
        }

        private async Task<(Stream, string)> GetStreamAsync(FileRange info, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            var client = _httpClientPool.Get();
            try
            {
                var request = new HttpRequestMessage { RequestUri = Target };
                request.Headers.ConnectionClose = false;
                request.Headers.Range = info.Range;

                var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);

                var stream = await response.Content.ReadAsStreamAsync();

                return (stream, info.FileName);
            }
            finally
            {
                _httpClientPool.Return(client);
            }
        }

        private async Task<Unit> WriteToFileAsync(Stream stream, string tempFileName, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            await using var fs = File.OpenWrite(tempFileName);
            await CopyStreamAsyncWithProgress(stream, fs, true, token);
            return Unit.Default;
        }

        private async ValueTask MergeFilesAsync(IEnumerable<FileRange> files, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            var dir = Path.GetDirectoryName(OutFileName);
            dir = EnsureDirectory(dir);
            var path = Path.Combine(dir, Path.GetFileName(OutFileName) ?? Path.GetRandomFileName());

            await using var outFileStream = File.Create(path);

            foreach (var file in files)
            {
                await using (var inputFileStream = File.OpenRead(file.FileName))
                {
                    await CopyStreamAsyncWithProgress(inputFileStream, outFileStream, false, token);
                }
                await DeleteFileWithRetryAsync(file.FileName, 3);
            }
        }

        private async ValueTask DeleteFileWithRetryAsync(string filename, byte retryTime)
        {
            var i = 0;
            while (true)
            {
                try
                {
                    File.Delete(filename);
                }
                catch (Exception) when (i < retryTime)
                {
                    ++i;
                    await Task.Delay(TimeSpan.FromSeconds(1));
                    continue;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $@"删除 {filename} 出错");
                }
                break;
            }
        }

        private async ValueTask CopyStreamAsyncWithProgress(Stream from, Stream to, bool reportSpeed, CancellationToken token, int bufferSize = 81920)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
            try
            {
                while (true)
                {
                    var length = await from.ReadAsync(buffer.AsMemory(), token);
                    if (length != 0)
                    {
                        await to.WriteAsync(buffer.AsMemory(0, length), token);
                        ReportProgress(length, reportSpeed);
                    }
                    else
                    {
                        break;
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private void ReportProgress(long length, bool reportSpeed)
        {
            if (reportSpeed)
            {
                Interlocked.Add(ref _last, length);
            }
            Interlocked.Add(ref _current, length);
            _progressUpdated.OnNext(Interlocked.Read(ref _current) / (double)_fileSize);
        }

        public ValueTask DisposeAsync()
        {
            _progressUpdated.OnCompleted();
            _currentSpeed.OnCompleted();

            return default;
        }
    }
}