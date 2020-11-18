using BilibiliApi.Model.AnchorInfo;
using BilibiliApi.Model.DanmuConf;
using BilibiliApi.Model.LiveRecordList;
using BilibiliApi.Model.LiveRecordUrl;
using BilibiliApi.Model.PlayUrl;
using BilibiliApi.Model.RoomInfo;
using BilibiliApi.Model.RoomInit;
using BilibiliLiveRecordDownLoader.Shared.HttpPolicy;
using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BilibiliApi.Clients
{
	public sealed class BililiveApiClient : IDisposable
	{
		public string UserAgent { get; set; } = @"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/87.0.4280.66 Safari/537.36";

		public string? Cookie { get; set; }

		private HttpClient _httpClient;
		private static readonly SemaphoreSlim SemaphoreSlim = new(1, 1);

		public BililiveApiClient()
		{
			_httpClient = BuildClient();
		}

		public void Reload()
		{
			_httpClient.Dispose();
			_httpClient = BuildClient();
		}

		/// <summary>
		/// 获取直播回放地址
		/// </summary>
		/// <param name="rid">视频id</param>
		/// <param name="token"></param>
		public async Task<LiveRecordUrlMessage?> GetLiveRecordUrlAsync(string rid, CancellationToken token = default)
		{
			var url = $@"https://api.live.bilibili.com/xlive/web-room/v1/record/getLiveRecordUrl?rid={rid}&platform=html5";
			return await GetJsonAsync<LiveRecordUrlMessage>(url, token);
		}

		/// <summary>
		/// 获取直播回放列表
		/// </summary>
		/// <param name="roomId">房间号（不允许短号）</param>
		/// <param name="page">页数</param>
		/// <param name="pageSize">每页大小</param>
		/// <param name="token"></param>
		public async Task<LiveRecordListMessage?> GetLiveRecordListAsync(long roomId, long page = 1, long pageSize = 20, CancellationToken token = default)
		{
			var url = $@"https://api.live.bilibili.com/xlive/web-room/v1/record/getList?room_id={roomId}&page={page}&page_size={pageSize}";
			return await GetJsonAsync<LiveRecordListMessage>(url, token);
		}

		/// <summary>
		/// 获取房间信息
		/// </summary>
		/// <param name="roomId">房间号（允许短号）</param>
		/// <param name="token"></param>
		/// <returns></returns>
		public async Task<RoomInitMessage?> GetRoomInitAsync(long roomId, CancellationToken token = default)
		{
			var url = $@"https://api.live.bilibili.com/room/v1/Room/room_init?id={roomId}";
			return await GetJsonAsync<RoomInitMessage>(url, token);
		}

		/// <summary>
		/// 获取直播间主播信息
		/// </summary>
		/// <param name="roomId">房间号（允许短号）</param>
		/// <param name="token"></param>
		/// <returns></returns>
		public async Task<AnchorInfoMessage?> GetAnchorInfoAsync(long roomId, CancellationToken token = default)
		{
			var url = $@"https://api.live.bilibili.com/live_user/v1/UserInfo/get_anchor_in_room?roomid={roomId}";
			return await GetJsonAsync<AnchorInfoMessage>(url, token);
		}

		/// <summary>
		/// 获取弹幕服务器地址
		/// </summary>
		/// <param name="roomId">房间号（允许短号）</param>
		/// <param name="token"></param>
		/// <returns></returns>
		public async Task<DanmuConfMessage?> GetDanmuConfAsync(long roomId, CancellationToken token = default)
		{
			var url = $@"https://api.live.bilibili.com/room/v1/Danmu/getConf?room_id={roomId}";
			return await GetJsonAsync<DanmuConfMessage>(url, token);
		}

		/// <summary>
		/// 获取直播间播放地址
		/// </summary>
		/// <param name="roomId">房间号（允许短号）</param>
		/// <param name="qn"></param>
		/// <param name="token"></param>
		/// <returns></returns>
		public async Task<PlayUrlMessage?> GetPlayUrlAsync(long roomId, long qn = 10000, CancellationToken token = default)
		{
			var url = $@"https://api.live.bilibili.com/room/v1/Room/playUrl?cid={roomId}&qn={qn}&platform=web";
			return await GetJsonAsync<PlayUrlMessage>(url, token);
		}

		/// <summary>
		/// 获取直播间详细信息
		/// </summary>
		/// <param name="roomId">房间号（允许短号）</param>
		/// <param name="token"></param>
		/// <returns></returns>
		public async Task<RoomInfoMessage?> GetRoomInfoAsync(long roomId, CancellationToken token = default)
		{
			var url = $@"https://api.live.bilibili.com/room/v1/Room/get_info?id={roomId}";
			return await GetJsonAsync<RoomInfoMessage>(url, token);
		}

		private async Task<T?> GetJsonAsync<T>(string url, CancellationToken token = default)
		{
			await SemaphoreSlim.WaitAsync(token);
			try
			{
				return await _httpClient.GetFromJsonAsync<T>(url, token);
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
				client = new(new ForceHttp2Handler(new SocketsHttpHandler()), true);
			}
			else
			{
				client = new(new ForceHttp2Handler(new SocketsHttpHandler { UseCookies = false }), true);
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
			_httpClient.Dispose();
		}
	}
}
