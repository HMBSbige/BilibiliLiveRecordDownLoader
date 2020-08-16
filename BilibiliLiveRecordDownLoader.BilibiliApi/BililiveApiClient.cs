using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BilibiliLiveRecordDownLoader.BilibiliApi.Model;

namespace BilibiliLiveRecordDownLoader.BilibiliApi
{
    public class BililiveApiClient : IDisposable
    {
        public string UserAgent { get; set; } = @"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/84.0.4147.125 Safari/537.36";

        public string Cookie { get; set; }

        private HttpClient _httpClient;
        private static readonly SemaphoreSlim SemaphoreSlim = new SemaphoreSlim(1, 1);

        public BililiveApiClient()
        {
            Reload();
        }

        public void Reload()
        {
            _httpClient?.Dispose();
            _httpClient = BuildClient();
        }

        /// <summary>
        /// 获取直播回放地址
        /// </summary>
        /// <param name="rid">视频id</param>
        /// <param name="token"></param>
        public async Task<LiveRecordUrl> GetLiveRecordUrl(string rid, CancellationToken token = default)
        {
            var url = $@"https://api.live.bilibili.com/xlive/web-room/v1/record/getLiveRecordUrl?rid={rid}&platform=html5";
            var jsonStream = await GetStreamAsync(url, token);
            var json = await JsonSerializer.DeserializeAsync<LiveRecordUrl>(jsonStream, cancellationToken: token);
            return json;
        }

        /// <summary>
        /// 获取直播回放列表
        /// </summary>
        /// <param name="roomId">房间号（不允许短号）</param>
        /// <param name="page">页数</param>
        /// <param name="pageSize">每页大小</param>
        /// <param name="token"></param>
        public async Task<LiveRecordList> GetLiveRecordList(long roomId, long page = 1, long pageSize = 20, CancellationToken token = default)
        {
            var url = $@"https://api.live.bilibili.com/xlive/web-room/v1/record/getList?room_id={roomId}&page={page}&page_size={pageSize}";
            var jsonStream = await GetStreamAsync(url, token);
            var json = await JsonSerializer.DeserializeAsync<LiveRecordList>(jsonStream, cancellationToken: token);
            return json;
        }

        /// <summary>
        /// 获取房间信息
        /// </summary>
        /// <param name="roomId"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task<RoomInit> GetRoomInit(long roomId, CancellationToken token = default)
        {
            var url = $@"https://api.live.bilibili.com/room/v1/Room/room_init?id={roomId}";
            var jsonStream = await GetStreamAsync(url, token);
            var json = await JsonSerializer.DeserializeAsync<RoomInit>(jsonStream, cancellationToken: token);
            return json;
        }

        public async Task<Stream> GetStreamAsync(string url, CancellationToken token = default)
        {
            await SemaphoreSlim.WaitAsync(token);
            try
            {
                var stream = await _httpClient.GetStreamAsync(url);
                return stream;
            }
            finally
            {
                SemaphoreSlim.Release();
            }
        }

        public async Task<string> GetStringAsync(string url, CancellationToken token = default)
        {
            await SemaphoreSlim.WaitAsync(token);
            try
            {
                var str = await _httpClient.GetStringAsync(url);
                Debug.WriteLine(str);
                return str;
            }
            finally
            {
                SemaphoreSlim.Release();
            }
        }

        private HttpClient BuildClient()
        {
            HttpClient client;
            if (string.IsNullOrEmpty(Cookie))
            {
                client = new HttpClient();
            }
            else
            {
                client = new HttpClient(new HttpClientHandler { UseCookies = false, UseDefaultCredentials = false }, true);
                client.DefaultRequestHeaders.Add(@"Cookie", Cookie);
            }

            client.DefaultRequestVersion = new Version(2, 0);
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.Add(@"Accept", @"application/json, text/javascript, */*; q=0.01");
            client.DefaultRequestHeaders.Add(@"Referer", @"https://live.bilibili.com/");
            client.DefaultRequestHeaders.Add(@"User-Agent", UserAgent);

            return client;
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
