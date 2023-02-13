using BilibiliApi.Model.PlayUrl;
using BilibiliApi.Model.RoomInfo;
using static BilibiliApi.Model.PlayUrl.RoomPlayInfo.Data.PlayurlInfo.Playurl.Stream.Format.Codec;

namespace BilibiliApi.Clients;

public partial class BilibiliApiClient
{
	#region 获取直播间播放地址

	/// <summary>
	/// 获取直播间播放地址
	/// </summary>
	/// <param name="roomId">房间号（允许短号）</param>
	/// <param name="qn"></param>
	/// <param name="token"></param>
	/// <returns></returns>
	public async Task<RoomPlayInfo?> GetRoomPlayInfoAsync(long roomId, long qn = 10000, CancellationToken token = default)
	{
		string url = $@"https://api.live.bilibili.com/xlive/web-room/v2/index/getRoomPlayInfo?room_id={roomId}&no_playurl=0&qn={qn}&platform=web&protocol=0&format=0&codec=0,1";
		return await GetJsonAsync<RoomPlayInfo>(url, token);
	}

	/// <summary>
	/// 获取直播间播放地址
	/// </summary>
	/// <param name="roomId">房间号（允许短号）</param>
	/// <param name="qn"></param>
	/// <param name="token"></param>
	/// <returns></returns>
	public async Task<string> GetRoomStreamUrlAsync(long roomId, long qn = 10000, CancellationToken token = default)
	{
		RoomPlayInfo? message = await GetRoomPlayInfoAsync(roomId, qn, token);

		RoomPlayInfo.Data.PlayurlInfo.Playurl.Stream.Format.Codec? codec = message?.data?.playurl_info?.playurl?
			.stream?.FirstOrDefault(x => x.protocol_name is @"http_stream")?
			.format?.FirstOrDefault(x => x.format_name is @"flv")?
			.codec?.FirstOrDefault(x => x.url_info?.FirstOrDefault(GetValidUrlInfo) is not null);

		if (message?.code is not 0 || codec is null)
		{
			if (message?.message is not null)
			{
				throw new HttpRequestException($@"[{roomId}] 获取直播地址失败: {message.message}");
			}
			throw new HttpRequestException($@"[{roomId}] 获取直播地址失败");
		}

		UrlInfo urlInfo = codec.url_info!.First(GetValidUrlInfo);

		return $@"{urlInfo.host}{codec.base_url}{urlInfo.extra}";

		static bool GetValidUrlInfo(UrlInfo x)
		{
			return !string.IsNullOrEmpty(x.host) && x.host.StartsWith(@"https://") && !x.host.Contains(@".mcdn.");
		}
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
		string url = $@"https://api.live.bilibili.com/xlive/web-room/v1/index/getInfoByRoom?room_id={roomId}";
		return await GetJsonAsync<RoomInfoMessage>(url, token);
	}

	/// <summary>
	/// 获取直播间详细信息
	/// </summary>
	/// <param name="roomId">房间号（允许短号）</param>
	/// <param name="token"></param>
	/// <returns></returns>
	public async Task<RoomInfoMessage.RoomInfoData> GetRoomInfoDataAsync(long roomId, CancellationToken token = default)
	{
		RoomInfoMessage? roomInfo = await GetRoomInfoAsync(roomId, token);
		if (roomInfo?.data is null || roomInfo.code != 0)
		{
			if (roomInfo?.message is not null)
			{
				throw new HttpRequestException($@"[{roomId}] 获取房间信息失败: {roomInfo.message}");
			}

			throw new HttpRequestException($@"[{roomId}] 获取房间信息失败");
		}
		return roomInfo.data;
	}

	#endregion

}
