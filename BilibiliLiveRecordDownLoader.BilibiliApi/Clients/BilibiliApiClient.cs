using BilibiliApi.Model.AnchorInfo;
using BilibiliApi.Model.DanmuConf;
using BilibiliApi.Model.LiveRecordList;
using BilibiliApi.Model.LiveRecordUrl;
using BilibiliApi.Model.PlayUrl;
using BilibiliApi.Model.RoomInfo;
using BilibiliApi.Model.RoomInit;
using BilibiliLiveRecordDownLoader.Shared.Interfaces;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BilibiliApi.Clients
{
	public partial class BilibiliApiClient : IHttpClient
	{
		public HttpClient Client { get; set; }

		private static readonly SemaphoreSlim SemaphoreSlim = new(1, 1);

		public BilibiliApiClient(HttpClient client)
		{
			Client = client;
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

		#region 获取直播间主播信息

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
		/// 获取直播间主播信息
		/// </summary>
		/// <param name="roomId">房间号（允许短号）</param>
		/// <param name="token"></param>
		/// <returns></returns>
		public async Task<AnchorInfo> GetAnchorInfoDataAsync(long roomId, CancellationToken token = default)
		{
			var roomInfo = await GetAnchorInfoAsync(roomId, token);
			if (roomInfo?.data?.info is null || roomInfo.code != 0)
			{
				if (roomInfo is not null)
				{
					throw new HttpRequestException($@"[{roomId}] 获取主播信息出错，可能该房间号的主播不存在: {roomInfo.message} {roomInfo.msg}");
				}

				throw new HttpRequestException($@"[{roomId}] 获取主播信息出错，可能该房间号的主播不存在");
			}

			return roomInfo.data.info;
		}

		#endregion

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

		#region 获取直播间播放地址

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
		/// 获取直播间播放地址
		/// </summary>
		/// <param name="roomId">房间号（允许短号）</param>
		/// <param name="qn"></param>
		/// <param name="token"></param>
		/// <returns></returns>
		public async Task<PlayUrlData> GetPlayUrlDataAsync(long roomId, long qn = 10000, CancellationToken token = default)
		{
			var message = await GetPlayUrlAsync(roomId, qn, token);
			if (message?.code != 0 || message.data?.durl?.FirstOrDefault()?.url is null)
			{
				if (message is not null)
				{
					throw new HttpRequestException($@"[{roomId}] 获取直播地址失败: {message.message}");
				}
				throw new HttpRequestException($@"[{roomId}] 获取直播地址失败");
			}
			return message.data;
		}

		#endregion

		#region 获取直播间详细信息

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

		/// <summary>
		/// 获取直播间详细信息
		/// </summary>
		/// <param name="roomId">房间号（允许短号）</param>
		/// <param name="token"></param>
		/// <returns></returns>
		public async Task<RoomInfoData> GetRoomInfoDataAsync(long roomId, CancellationToken token = default)
		{
			var roomInfo = await GetRoomInfoAsync(roomId, token);
			if (roomInfo?.data is null || roomInfo.code != 0)
			{
				if (roomInfo is not null)
				{
					throw new HttpRequestException($@"[{roomId}] 获取房间信息失败: {roomInfo.message} {roomInfo.msg}");
				}

				throw new HttpRequestException($@"[{roomId}] 获取房间信息失败");
			}
			return roomInfo.data;
		}

		#endregion

		private async Task<T?> GetJsonAsync<T>(string url, CancellationToken token)
		{
			await SemaphoreSlim.WaitAsync(token);
			try
			{
				return await Client.GetFromJsonAsync<T>(url, token);
			}
			finally
			{
				SemaphoreSlim.Release();
			}
		}

		private async Task<HttpResponseMessage> PostAsync(string url, HttpContent content, CancellationToken token)
		{
			await SemaphoreSlim.WaitAsync(token);
			try
			{
				return await Client.PostAsync(url, content, token);
			}
			finally
			{
				SemaphoreSlim.Release();
			}
		}
	}
}
