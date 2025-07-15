using BilibiliApi.Enums;
using BilibiliApi.Model;
using BilibiliApi.Model.PlayUrl;
using DynamicData;
using System.Text.Json;

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
		string url = $@"https://api.live.bilibili.com/xlive/web-room/v2/index/getRoomPlayInfo?room_id={roomId}&no_playurl=0&qn={qn}&platform=web&protocol=0,1&format=0,1,2&codec=0,1,2";
		return await GetJsonAsync<RoomPlayInfo>(url, token);
	}

	public const string DefaultCodecOrder = @"avc;hevc;av1";
	public const string DefaultFormatOrder = @"fmp4;ts;flv";

	private record StreamUriInfo(string Protocol, string Format, RoomPlayInfoStreamCodec Codec);

	public async Task<(Uri[], string)> GetRoomStreamUriAsync(long roomId, long qn = 10000, string? codecOrder = default, string? formatOrder = default, CancellationToken cancellationToken = default)
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

		RoomPlayInfoStream[] playInfo = message.Data.PlayUrlInfo?.PlayUrl?.StreamInfo ?? throw new HttpRequestException(@"获取直播地址失败: 无法找到直播流");

		List<StreamUriInfo> list = [];

		foreach (RoomPlayInfoStream streamInfo in playInfo)
		{
			if (streamInfo.Format is null || streamInfo.ProtocolName is null)
			{
				continue;
			}

			foreach (RoomPlayInfoStreamFormat format in streamInfo.Format)
			{
				if (format.Codec is null || format.FormatName is null)
				{
					continue;
				}

				foreach (RoomPlayInfoStreamCodec codec in format.Codec)
				{
					if (codec.BaseUrl is null || codec.UrlInfo?.FirstOrDefault() is null || codec.CodecName is null)
					{
						continue;
					}

					if (codec.CodecName.Equals(@"hevc", StringComparison.OrdinalIgnoreCase) && format.FormatName.Equals(@"flv", StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}

					list.Add(new StreamUriInfo(streamInfo.ProtocolName, format.FormatName, codec));
				}
			}
		}

		if (!list.Any())
		{
			throw new HttpRequestException(@"获取直播地址失败: 无法找到直播流");
		}

		string[] codecOrderByDescending = GetOrderByDescending(codecOrder, DefaultCodecOrder);
		string[] formatOrderByDescending = GetOrderByDescending(formatOrder, DefaultFormatOrder);

		StreamUriInfo info = list.OrderByDescending(x => codecOrderByDescending.IndexOf(x.Codec.CodecName, StringComparer.OrdinalIgnoreCase))
			.ThenByDescending(x => formatOrderByDescending.IndexOf(x.Format, StringComparer.OrdinalIgnoreCase))
			.First();

		RoomPlayInfoStreamUrlInfo[] uriInfo = info.Codec.UrlInfo!.Where(GetValidUrlInfo).ToArray();

		Uri[] result = new Uri[uriInfo.LongLength];

		string baseUrl = info.Codec.BaseUrl!;

		for (long i = 0; i < result.LongLength; ++i)
		{
			result[i] = new Uri(uriInfo[i].Host + baseUrl + uriInfo[i].Extra);
		}

		return (result, info.Format);

		static string[] GetOrderByDescending(string? order, string defaultValue)
		{
			while (true)
			{
				if (string.IsNullOrWhiteSpace(order))
				{
					order = defaultValue;
					continue;
				}

				string[] r = order.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

				if (r.Length is not 0)
				{
					return r.Reverse().ToArray();
				}

				order = defaultValue;
			}
		}

		static bool GetValidUrlInfo(RoomPlayInfoStreamUrlInfo x)
		{
			return !string.IsNullOrEmpty(x.Host) && x.Host.StartsWith(@"https://");
		}
	}

	#endregion

	#region 获取直播间详细信息

	/// <summary>
	/// 获取直播间详细信息
	/// </summary>
	/// <param name="roomId">房间号（允许短号</param>
	/// <param name="cancellationToken"></param>
	/// <returns></returns>
	public async ValueTask<LiveRoomInfo> GetLiveRoomInfoAsync(long roomId, CancellationToken cancellationToken = default)
	{
		try
		{
			string url = $@"https://api.live.bilibili.com/room/v1/Room/get_info?id={roomId}";

			JsonDocument? json = await GetJsonAsync<JsonDocument>(url, cancellationToken);

			if (json is null)
			{
				throw ThrowException();
			}

			JsonElement root = json.RootElement;

			long code = root.GetProperty(@"code").GetInt64();
			string? msg = root.GetProperty(@"msg").GetString();
			string? message = root.GetProperty(@"message").GetString();

			if (code is not 0)
			{
				throw ThrowException(msg ?? message);
			}

			JsonElement data = root.GetProperty(@"data");
			LiveRoomInfo res = new()
			{
				ShortId = data.GetProperty(@"short_id").GetInt64(),
				RoomId = data.GetProperty(@"room_id").GetInt64(),
				LiveStatus = (LiveStatus)data.GetProperty(@"live_status").GetInt32(),
				Title = data.GetProperty(@"title").GetString(),
				UserId = data.GetProperty(@"uid").GetInt64()
			};

			url = $@"https://api.live.bilibili.com/xlive/web-room/v1/index/getDanmuMedalAnchorInfo?ruid={res.UserId}";

			json = await GetJsonAsync<JsonDocument>(url, cancellationToken);

			if (json is null)
			{
				throw ThrowException();
			}

			root = json.RootElement;

			code = root.GetProperty(@"code").GetInt64();
			message = root.GetProperty(@"message").GetString();

			if (code is not 0)
			{
				throw ThrowException(message);
			}

			data = root.GetProperty(@"data");

			res.UserName = data.GetProperty(@"runame").GetString();
			res.LiveStatus = (LiveStatus)data.GetProperty(@"live_stream_status").GetInt32();

			return res;
		}
		catch (Exception ex) when (ex is not HttpRequestException)
		{
			throw ThrowException();
		}

		Exception ThrowException(string? msg = null)
		{
			if (msg is not null)
			{
				throw new HttpRequestException($@"获取房间信息失败: {msg}");
			}

			throw new HttpRequestException(@"获取房间信息失败");
		}
	}

	#endregion
}
