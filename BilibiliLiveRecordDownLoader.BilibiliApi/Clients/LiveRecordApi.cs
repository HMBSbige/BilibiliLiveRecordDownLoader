using BilibiliApi.Model.LiveRecordDanmu;
using BilibiliApi.Model.LiveRecordDanmuList;
using BilibiliApi.Model.LiveRecordList;
using BilibiliApi.Model.LiveRecordUrl;
using System.Threading;
using System.Threading.Tasks;

namespace BilibiliApi.Clients
{
	public partial class BilibiliApiClient
	{
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
		/// 获取直播回放弹幕信息
		/// </summary>
		/// <param name="rid">视频id</param>
		/// <param name="token"></param>
		public async Task<LiveRecordDanmuListMessage?> GetDanmuInfoByLiveRecordAsync(string rid, CancellationToken token = default)
		{
			var url = $@"https://api.live.bilibili.com/xlive/web-room/v1/record/getInfoByLiveRecord?rid={rid}";
			return await GetJsonAsync<LiveRecordDanmuListMessage>(url, token);
		}

		/// <summary>
		/// 获取直播回放弹幕
		/// </summary>
		/// <param name="rid">视频id</param>
		/// <param name="index">页数</param>
		/// <param name="token"></param>
		public async Task<LiveRecordDanmuMessage?> GetDmMsgByPlayBackIdAsync(string rid, long index, CancellationToken token = default)
		{
			var url = $@"https://api.live.bilibili.com/xlive/web-room/v1/dM/getDMMsgByPlayBackID?rid={rid}&index={index}";
			return await GetJsonAsync<LiveRecordDanmuMessage>(url, token);
		}
	}
}
