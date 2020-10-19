using BilibiliLiveRecordDownLoader.Http.HttpPolicy;
using Microsoft.Extensions.ObjectPool;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;

namespace BilibiliLiveRecordDownLoader.Http.DownLoaders
{
    public class MultiThreadedDownloader : IDownloader
    {
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

        public MultiThreadedDownloader()
        {
            ServicePointManager.DefaultConnectionLimit = 10000;

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
        private async Task<long> GetContentLengthAsync(CancellationToken token)
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

        public ValueTask DownloadAsync(CancellationToken token)
        {
            throw new NotImplementedException();
        }

        public ValueTask DisposeAsync()
        {
            _progressUpdated.OnCompleted();
            _currentSpeed.OnCompleted();

            return default;
        }
    }
}