using BilibiliLive.Api;
using BilibiliLive.Http;
using BilibiliLive.Wbi;
using System.Globalization;
using System.Text.Json.Serialization.Metadata;

namespace BilibiliLive;

public sealed class BilibiliApiClient
{
	private const string LiveApi = "https://api.live.bilibili.com/";
	private const string NavUrl = "https://api.bilibili.com/x/web-interface/nav";
	private const string PassportApi = "https://passport.bilibili.com/x/passport-login/web/qrcode/";

	private const long NotLoggedInCode = -101;
	private const long RoomNotFoundCode = 60004;

	private readonly WbiKeyCache _wbiKeys;

	public BilibiliApiClient(HttpClient httpClient, TimeProvider? timeProvider = null)
	{
		HttpClient = httpClient;
		TimeProvider = timeProvider ?? TimeProvider.System;
		_wbiKeys = new WbiKeyCache(FetchMixinKeyAsync, TimeProvider);
	}

	internal HttpClient HttpClient { get; }

	internal TimeProvider TimeProvider { get; }

	public async Task<LiveRoomInfo> GetRoomInfoAsync(long roomId, CancellationToken cancellationToken = default)
	{
		// getRoomBaseInfo resolves short ids and is the only unsigned endpoint that also returns the streamer name
		Uri uri = CreateUri($"{LiveApi}xlive/web-room/v1/index/getRoomBaseInfo?room_ids={roomId}&req_biz=web_room_componet");
		RoomBaseInfoData data = await GetDataAsync(uri, BilibiliJsonContext.Default.RoomBaseInfoData, cancellationToken);

		if (data.ByRoomIds?.Values.FirstOrDefault() is not { } room)
		{
			throw new BilibiliApiException($"Room {roomId} does not exist.", RoomNotFoundCode);
		}

		return new LiveRoomInfo(room.RoomId, room.ShortId, room.Uid, room.Uname ?? string.Empty, room.Title ?? string.Empty, ToLiveStatus(room.LiveStatus));
	}

	public async Task<IReadOnlyList<LiveStreamCandidate>> GetStreamCandidatesAsync(long roomId, long qn, IReadOnlyList<StreamCodec> codecPriority, IReadOnlyList<StreamFormat> formatPriority, CancellationToken cancellationToken = default)
	{
		Uri uri = CreateUri($"{LiveApi}xlive/web-room/v2/index/getRoomPlayInfo?room_id={roomId}&no_playurl=0&mask=1&qn={qn}&platform=web&protocol=0,1&format=0,1,2&codec=0,1,2");
		RoomPlayInfoData data = await GetDataAsync(uri, BilibiliJsonContext.Default.RoomPlayInfoData, cancellationToken);

		if (ToLiveStatus(data.LiveStatus) is not LiveStatus.Live)
		{
			throw new BilibiliApiException($"Room {roomId} is not live.");
		}

		LiveStreamCandidate[] candidates = StreamCandidates.Select(data, codecPriority, formatPriority);

		if (candidates.Length is 0)
		{
			throw new BilibiliApiException($"Room {roomId} has no usable stream.");
		}

		return candidates;
	}

	public Task<DanmuServerInfo> GetDanmuServerInfoAsync(long roomId, CancellationToken cancellationToken = default)
	{
		return GetDanmuServerInfoCoreAsync(roomId, false, cancellationToken);
	}

	// The token of an anonymous request only authenticates uid 0
	internal Task<DanmuServerInfo> GetAnonymousDanmuServerInfoAsync(long roomId, CancellationToken cancellationToken)
	{
		return GetDanmuServerInfoCoreAsync(roomId, true, cancellationToken);
	}

	private async Task<DanmuServerInfo> GetDanmuServerInfoCoreAsync(long roomId, bool anonymous, CancellationToken cancellationToken)
	{
		Uri uri = await CreateSignedUriAsync($"{LiveApi}xlive/web-room/v1/index/getDanmuInfo", [new("id", roomId.ToString(CultureInfo.InvariantCulture)), new("type", "0"), new("web_location", "444.8")], cancellationToken);
		using HttpRequestMessage request = new(HttpMethod.Get, uri);
		request.Options.Set(BilibiliRequestHandler.AnonymousOption, anonymous);
		DanmuInfoData data = await GetDataAsync(request, BilibiliJsonContext.Default.DanmuInfoData, cancellationToken);

		List<DanmuHost> hosts = [];

		foreach (DanmuInfoHost host in data.HostList ?? [])
		{
			if (host is { Host: { Length: > 0 } name, WssPort: not 0 and var port })
			{
				hosts.Add(new DanmuHost(name, port));
			}
		}

		return new DanmuServerInfo(data.Token ?? string.Empty, hosts);
	}

	public async Task<long> GetCurrentUserIdAsync(CancellationToken cancellationToken = default)
	{
		ApiResponse<NavData> response = await GetNavAsync(cancellationToken);

		if (response.Code is NotLoggedInCode)
		{
			return 0;
		}

		NavData data = response.GetData();
		return data.IsLogin ? data.Mid : 0;
	}

	public async Task<bool> IsLoggedInAsync(CancellationToken cancellationToken = default)
	{
		return await GetCurrentUserIdAsync(cancellationToken) is not 0;
	}

	public async Task<QrCodeLoginTicket> CreateQrCodeLoginAsync(CancellationToken cancellationToken = default)
	{
		QrCodeGenerateData data = await GetDataAsync(new Uri($"{PassportApi}generate"), BilibiliJsonContext.Default.QrCodeGenerateData, cancellationToken);

		if (string.IsNullOrEmpty(data.Url) || string.IsNullOrEmpty(data.QrcodeKey))
		{
			throw new BilibiliApiException("The QR code login response is incomplete.");
		}

		return new QrCodeLoginTicket(data.Url, data.QrcodeKey);
	}

	public async Task<QrCodeLoginResult> PollQrCodeLoginAsync(string key, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrEmpty(key);

		Uri uri = new($"{PassportApi}poll?qrcode_key={Uri.EscapeDataString(key)}&source=main-fe-header");
		using HttpResponseMessage response = await HttpClient.GetAsync(uri, cancellationToken);
		QrCodePollData data = (await ApiResponse.ReadAsync(response, BilibiliJsonContext.Default.QrCodePollData, cancellationToken)).GetData();

		return data.Code switch
		{
			0 => new QrCodeLoginResult(QrCodeLoginState.Succeeded, GetLoginCookie(response)),
			86038 => new QrCodeLoginResult(QrCodeLoginState.Expired),
			86090 => new QrCodeLoginResult(QrCodeLoginState.WaitingForConfirm),
			86101 => new QrCodeLoginResult(QrCodeLoginState.WaitingForScan),
			_ => throw new BilibiliApiException(data.Message ?? $"Unknown QR code login state {data.Code}.", data.Code)
		};

		static string GetLoginCookie(HttpResponseMessage response)
		{
			string cookie = response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? values) ? CookieHeader.FromSetCookie(values) : string.Empty;
			return cookie.Length is 0 ? throw new BilibiliApiException("The QR code login succeeded without setting any cookie.") : cookie;
		}
	}

	private static LiveStatus ToLiveStatus(int status)
	{
		return status switch
		{
			0 => LiveStatus.Idle,
			1 => LiveStatus.Live,
			2 => LiveStatus.Round,
			_ => LiveStatus.Unknown
		};
	}

	private static Uri CreateUri(FormattableString url)
	{
		return new Uri(url.ToString(CultureInfo.InvariantCulture));
	}

	private async ValueTask<Uri> CreateSignedUriAsync(string url, IEnumerable<KeyValuePair<string, string>> parameters, CancellationToken cancellationToken)
	{
		string mixinKey = await _wbiKeys.GetMixinKeyAsync(cancellationToken);
		return new Uri($"{url}?{WbiSigner.Sign(parameters, mixinKey, TimeProvider.GetUtcNow().ToUnixTimeSeconds())}");
	}

	private async Task<T> GetDataAsync<T>(Uri uri, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken) where T : class
	{
		using HttpRequestMessage request = new(HttpMethod.Get, uri);
		return await GetDataAsync(request, typeInfo, cancellationToken);
	}

	private async Task<T> GetDataAsync<T>(HttpRequestMessage request, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken) where T : class
	{
		using HttpResponseMessage response = await HttpClient.SendAsync(request, cancellationToken);
		return (await ApiResponse.ReadAsync(response, typeInfo, cancellationToken)).GetData();
	}

	private async Task<ApiResponse<NavData>> GetNavAsync(CancellationToken cancellationToken)
	{
		using HttpResponseMessage response = await HttpClient.GetAsync(new Uri(NavUrl), cancellationToken);
		return await ApiResponse.ReadAsync(response, BilibiliJsonContext.Default.NavData, cancellationToken);
	}

	private async Task<string> FetchMixinKeyAsync(CancellationToken cancellationToken)
	{
		ApiResponse<NavData> response = await GetNavAsync(cancellationToken);

		// nav answers -101 to anonymous users but still carries the keys
		if (response.Code is not (0 or NotLoggedInCode))
		{
			throw new BilibiliApiException(response.Message, response.Code);
		}

		if (response.Data?.WbiImg is not { ImgUrl: { } imgUrl, SubUrl: { } subUrl })
		{
			throw new BilibiliApiException("The nav response contains no wbi keys.", response.Code);
		}

		return WbiSigner.GetMixinKey(GetKey(imgUrl), GetKey(subUrl));

		static string GetKey(string url)
		{
			return Path.GetFileNameWithoutExtension(new Uri(url).AbsolutePath);
		}
	}
}
