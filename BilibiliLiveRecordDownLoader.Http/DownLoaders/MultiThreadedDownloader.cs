using BilibiliLiveRecordDownLoader.Http.HttpPolicy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;

namespace BilibiliLiveRecordDownLoader.Http.DownLoaders
{
    public class MultiThreadedDownloader : IDownloader
    {
        private readonly ILogger _logger;

        private readonly BehaviorSubject<double> _progressUpdated = new BehaviorSubject<double>(0.0);
        public IObservable<double> ProgressUpdated => _progressUpdated.AsObservable();

        private readonly BehaviorSubject<long> _currentSpeed = new BehaviorSubject<long>(0);
        public IObservable<long> CurrentSpeed => _currentSpeed.AsObservable();

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

        public MultiThreadedDownloader(ILogger logger)
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

        public async ValueTask DownloadAsync(CancellationToken token)
        {
            var length = await GetContentLengthAsync(token); //总大小

            TempDir = EnsureDirectory(TempDir);
            var list = GetFileRangeList(length);


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

        private IEnumerable<FileRange> GetFileRangeList(long length)
        {
            var list = new List<FileRange>();

            var parts = Threads; //线程数
            var partSize = length / parts; //每块大小

            _logger.LogDebug($@"总大小：{length} ({Target})");
            _logger.LogDebug($@"每块大小：{partSize} ({Target})");

            for (var i = 1; i < parts; ++i)
            {
                var range = new RangeHeaderValue((i - 1) * partSize, i * partSize);
                list.Add(new FileRange { FileName = GetTempFileName(), Range = range });
            }

            var last = new RangeHeaderValue((parts - 1) * partSize, length);
            list.Add(new FileRange { FileName = GetTempFileName(), Range = last });

            return list;
        }

        public ValueTask DisposeAsync()
        {
            _progressUpdated.OnCompleted();
            _currentSpeed.OnCompleted();

            return default;
        }
    }
}