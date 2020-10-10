using BilibiliApi.Model.AnchorInfo;
using BilibiliApi.Model.DanmuConf;
using BilibiliApi.Model.LiveRecordList;
using BilibiliApi.Model.LiveRecordUrl;
using BilibiliApi.Model.RoomInit;
using BilibiliApi.Utils;
using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BilibiliApi.Clients
{
    public class BililiveApiClient : IDisposable
    {
        public string UserAgent { get; set; } = @"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/85.0.4183.121 Safari/537.36";

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
        public async Task<LiveRecordUrlMessage> GetLiveRecordUrl(string rid, CancellationToken token = default)
        {
            var url = $@"https://api.live.bilibili.com/xlive/web-room/v1/record/getLiveRecordUrl?rid={rid}&platform=html5";
            await using var jsonStream = await GetStreamAsync(url, token);
            var json = await JsonSerializer.DeserializeAsync<LiveRecordUrlMessage>(jsonStream, cancellationToken: token);
            return json;
        }

        /// <summary>
        /// 获取直播回放列表
        /// </summary>
        /// <param name="roomId">房间号（不允许短号）</param>
        /// <param name="page">页数</param>
        /// <param name="pageSize">每页大小</param>
        /// <param name="token"></param>
        public async Task<LiveRecordListMessage> GetLiveRecordList(long roomId, long page = 1, long pageSize = 20, CancellationToken token = default)
        {
            var url = $@"https://api.live.bilibili.com/xlive/web-room/v1/record/getList?room_id={roomId}&page={page}&page_size={pageSize}";
            await using var jsonStream = await GetStreamAsync(url, token);
            var json = await JsonSerializer.DeserializeAsync<LiveRecordListMessage>(jsonStream, cancellationToken: token);
            return json;
        }

        /// <summary>
        /// 获取房间信息
        /// </summary>
        /// <param name="roomId">房间号（允许短号）</param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task<RoomInitMessage> GetRoomInit(long roomId, CancellationToken token = default)
        {
            var url = $@"https://api.live.bilibili.com/room/v1/Room/room_init?id={roomId}";
            await using var jsonStream = await GetStreamAsync(url, token);
            var json = await JsonSerializer.DeserializeAsync<RoomInitMessage>(jsonStream, cancellationToken: token);
            return json;
        }

        /// <summary>
        /// 获取直播间主播信息
        /// </summary>
        /// <param name="roomId">房间号（允许短号）</param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task<AnchorInfoMessage> GetAnchorInfo(long roomId, CancellationToken token = default)
        {
            var url = $@"https://api.live.bilibili.com/live_user/v1/UserInfo/get_anchor_in_room?roomid={roomId}";
            await using var jsonStream = await GetStreamAsync(url, token);
            var json = await JsonSerializer.DeserializeAsync<AnchorInfoMessage>(jsonStream, cancellationToken: token);
            return json;
        }

        /// <summary>
        /// 获取弹幕服务器地址
        /// </summary>
        /// <param name="roomId">房间号（理论上不允许短号，目前实测任意都可以）</param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task<DanmuConfMessage> GetDanmuConf(long roomId, CancellationToken token = default)
        {
            var url = $@"https://api.live.bilibili.com/room/v1/Danmu/getConf?room_id={roomId}";
            await using var jsonStream = await GetStreamAsync(url, token);
            var json = await JsonSerializer.DeserializeAsync<DanmuConfMessage>(jsonStream, cancellationToken: token);
            return json;
        }

        private async Task<Stream> GetStreamAsync(string url, CancellationToken token = default)
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

        private HttpClient BuildClient()
        {
            HttpClient client;
            if (string.IsNullOrEmpty(Cookie))
            {
                client = new HttpClient(new ForceHttp2Handler(new SocketsHttpHandler()), true);
            }
            else
            {
                client = new HttpClient(new ForceHttp2Handler(new SocketsHttpHandler { UseCookies = false }), true);
                client.DefaultRequestHeaders.Add(@"Cookie", Cookie);
            }

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
