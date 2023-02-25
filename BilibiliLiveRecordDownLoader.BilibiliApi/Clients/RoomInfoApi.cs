using BilibiliApi.Enums;
using BilibiliApi.Model.PlayUrl;
using BilibiliApi.Model.RoomInfo;

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
		string url = $@"https://api.live.bilibili.com/xlive/web-room/v2/index/getRoomPlayInfo?room_id={roomId}&no_playurl=0&qn={qn}&platform=web&protocol=0,1&format=0,1,2&codec=0";
		return await GetJsonAsync<RoomPlayInfo>(url, token);
	}

	/// <summary>
	/// 获取直播间播放地址（HTTP-FLV）
	/// </summary>
	/// <param name="roomId">房间号（允许短号）</param>
	/// <param name="qn">画质id</param>
	/// <param name="cancellationToken"></param>
	public async Task<Uri> GetRoomStreamUriAsync(long roomId, long qn = 10000, CancellationToken cancellationToken = default)
	{
		RoomPlayInfo? message = await GetRoomPlayInfoAsync(roomId, qn, cancellationToken);

		if (message?.Code is not 0)
		{
			if (message?.Message is not null)
			{
				throw new HttpRequestException($@"获取直播地址失败: {message.Message}");
			}
			throw new HttpRequestException(@"获取直播地址失败");
		}

		if (message.Data?.LiveStatus is not LiveStatus.直播)
		{
			throw new HttpRequestException(@"直播间未在直播");
		}

		RoomPlayInfoStreamCodec codec = message.Data.PlayUrlInfo?.PlayUrl?
			.StreamInfo?.FirstOrDefault(x => x.ProtocolName is @"http_stream")?
			.Format?.FirstOrDefault(x => x.FormatName is @"flv")?
			.Codec?.FirstOrDefault(x => x.UrlInfo?.FirstOrDefault(GetValidUrlInfo) is not null) ?? throw new HttpRequestException(@"获取直播地址失败: 无法找到 FLV 流");

		RoomPlayInfoStreamUrlInfo urlInfo = codec.UrlInfo!.First(GetValidUrlInfo);

		return new Uri(urlInfo.Host + codec.BaseUrl + urlInfo.Extra);

		static bool GetValidUrlInfo(RoomPlayInfoStreamUrlInfo x)
		{
			return !string.IsNullOrEmpty(x.Host) && x.Host.StartsWith(@"https://") && !x.Host.Contains(@".mcdn.");
		}
	}

	/// <summary>
	/// 获取直播间播放地址（HLS）
	/// </summary>
	/// <param name="roomId">房间号（允许短号）</param>
	/// <param name="formatName">TS/fMP4</param>
	/// <param name="qn">画质id</param>
	/// <param name="cancellationToken"></param>
	public async Task<Uri[]> GetRoomHlsUriAsync(long roomId, string formatName = @"TS", long qn = 10000, CancellationToken cancellationToken = default)
	{
		RoomPlayInfo? message = await GetRoomPlayInfoAsync(roomId, qn, cancellationToken);

		if (message?.Code is not 0)
		{
			if (message?.Message is not null)
			{
				throw new HttpRequestException($@"获取直播地址失败: {message.Message}");
			}
			throw new HttpRequestException(@"获取直播地址失败");
		}

		if (message.Data?.LiveStatus is not LiveStatus.直播)
		{
			throw new HttpRequestException(@"直播间未在直播");
		}

		RoomPlayInfoStreamFormat[]? formats = message.Data.PlayUrlInfo?.PlayUrl?
			.StreamInfo?.FirstOrDefault(x => x.ProtocolName is @"http_hls")?
			.Format;

		if (formats is null || !formats.Any())
		{
			throw new HttpRequestException(@"获取直播地址失败: 无法找到 HLS 流");
		}

		RoomPlayInfoStreamCodec? codecs = formats.FirstOrDefault(x => formatName.Equals(x.FormatName, StringComparison.OrdinalIgnoreCase))?.Codec?.FirstOrDefault();

		if (codecs?.UrlInfo?.FirstOrDefault() is null || codecs.BaseUrl is null)
		{
			throw new HttpRequestException($@"获取直播地址失败: 无法找到 HLS-{formatName} 流");
		}

		string baseUrl = codecs.BaseUrl.Replace(@"_bluray", string.Empty);

		Uri[] result = new Uri[codecs.UrlInfo.Length];

		for (int i = 0; i < codecs.UrlInfo.Length; ++i)
		{
			result[i] = new Uri(codecs.UrlInfo[i].Host + baseUrl + codecs.UrlInfo[i].Extra);
		}

		return result;
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
				throw new HttpRequestException($@"获取房间信息失败: {roomInfo.message}");
			}

			throw new HttpRequestException(@"获取房间信息失败");
		}
		return roomInfo.data;
	}

	#endregion

}
