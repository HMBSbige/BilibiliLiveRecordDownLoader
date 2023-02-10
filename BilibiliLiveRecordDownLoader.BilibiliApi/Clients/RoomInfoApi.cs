using BilibiliApi.Model.AnchorInfo;
using BilibiliApi.Model.PlayUrl;
using BilibiliApi.Model.RoomInfo;
using BilibiliApi.Model.RoomInit;

namespace BilibiliApi.Clients;

public partial class BilibiliApiClient
{
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

}
