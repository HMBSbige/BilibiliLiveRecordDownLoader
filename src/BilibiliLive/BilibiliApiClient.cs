using BilibiliLive.Api;
using BilibiliLive.Danmu;
using BilibiliLive.Http;
using BilibiliLive.Wbi;
using CryptoBase.Rsa;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization.Metadata;

namespace BilibiliLive;

public sealed class BilibiliApiClient
{
	private const string LiveApi = "https://api.live.bilibili.com/";
	private const string NavUrl = "https://api.bilibili.com/x/web-interface/nav";
	private const string PassportApi = "https://passport.bilibili.com/x/passport-login/web/";
	private const string LogoutUrl = "https://passport.bilibili.com/login/exit/v2";
	private const string CorrespondUrl = "https://www.bilibili.com/correspond/1/";

	private static readonly TimeSpan BeijingOffset = TimeSpan.FromHours(8);

	// 文档写明 page_size 只接受 1 到 10
	private const int FollowingPageSize = 10;

	private readonly WbiKeyCache _wbiKeys;

	public BilibiliApiClient(HttpClient httpClient, TimeProvider? timeProvider = null)
	{
		HttpClient = httpClient;
		TimeProvider = timeProvider ?? TimeProvider.System;
		_wbiKeys = new WbiKeyCache(FetchMixinKeyAsync, TimeProvider);
	}

	internal HttpClient HttpClient { get; }

	internal TimeProvider TimeProvider { get; }

	internal RsaKey CorrespondKey { get; init; } = CorrespondPage.PublicKey;

	internal static TimeSpan FollowingPageInterval { get; } = TimeSpan.FromMilliseconds(300);

	public async Task<LiveRoomInfo> GetRoomInfoAsync(long roomId, CancellationToken cancellationToken = default)
	{
		IReadOnlyList<LiveRoomInfo> rooms = await GetRoomInfosAsync([roomId], cancellationToken);
		return rooms is [var room, ..] ? room : throw new BilibiliApiException($"Room {roomId} does not exist.", BilibiliApiException.RoomNotFoundCode);
	}

	/// <returns>不存在的房间不在结果中</returns>
	public async Task<IReadOnlyList<LiveRoomInfo>> GetRoomInfosAsync(IReadOnlyCollection<long> roomIds, CancellationToken cancellationToken = default)
	{
		if (roomIds.Count is 0)
		{
			throw new ArgumentException("At least one room id is required.", nameof(roomIds));
		}

		RoomBaseInfoData data = await GetDataAsync(CreateRoomBaseInfoUri(roomIds), BilibiliJsonContext.Default.RoomBaseInfoData, cancellationToken);
		return data.ByRoomIds is { } rooms ? [.. rooms.Values.Select(ToRoomInfo)] : [];
	}

	// getRoomBaseInfo 能解析短号，也是唯一不用签名又返回主播名的接口
	private static Uri CreateRoomBaseInfoUri(IReadOnlyCollection<long> roomIds)
	{
		DefaultInterpolatedStringHandler url = new(0, 0, CultureInfo.InvariantCulture);
		url.AppendLiteral($"{LiveApi}xlive/web-room/v1/index/getRoomBaseInfo?");

		foreach (long roomId in roomIds)
		{
			url.AppendLiteral("room_ids=");
			url.AppendFormatted(roomId);
			url.AppendLiteral("&");
		}

		url.AppendLiteral("req_biz=web_room_componet");
		return new Uri(url.ToStringAndClear());
	}

	private static LiveRoomInfo ToRoomInfo(RoomBaseInfo room)
	{
		return new LiveRoomInfo(room.RoomId, room.ShortId, room.Uid, room.Uname ?? string.Empty, room.Title ?? string.Empty, ToLiveStatus(room.LiveStatus))
		{
			AreaName = room.AreaName ?? string.Empty,
			ParentAreaName = room.ParentAreaName ?? string.Empty,
			LiveStartTime = ParseBeijingTime(room.LiveTime),
			LiveId = room.LiveIdStr ?? string.Empty
		};
	}

	// live_time 不带时区，总是北京时间；从未开播的房间返回全零
	private static DateTimeOffset? ParseBeijingTime(string? value)
	{
		return DateTime.TryParseExact(value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime time) ? new DateTimeOffset(time, BeijingOffset) : null;
	}

	public async IAsyncEnumerable<FollowedLiveRoom> GetFollowedRoomsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		HashSet<long> seen = [];

		for (int page = 1; ; ++page)
		{
			if (page > 1)
			{
				await Task.Delay(FollowingPageInterval, TimeProvider, cancellationToken);
			}

			Uri uri = CreateUri($"{LiveApi}xlive/web-ucenter/user/following?page={page}&page_size={FollowingPageSize}&ignoreRecord=1&hit_ab=true");
			FollowingData data = await GetDataAsync(uri, BilibiliJsonContext.Default.FollowingData, cancellationToken);
			bool hasNewRoom = false;

			foreach (FollowingRoom room in data.List ?? [])
			{
				// 翻页期间条目可能在页与页之间移动，同一个房间可能出现两次
				if (room.RoomId > 0 && seen.Add(room.RoomId))
				{
					hasNewRoom = true;
					yield return new FollowedLiveRoom(room.RoomId, room.Uname ?? string.Empty, room.Title ?? string.Empty, ToLiveStatus(room.LiveStatus));
				}
			}

			// 没有 totalPage 时，末页之后的页可能重复最后一页，所以遇到没有新房间的页就停
			if (data.List is not { Count: > 0 } || (data.TotalPage > 0 ? page >= data.TotalPage : !hasNewRoom))
			{
				yield break;
			}
		}
	}

	public async Task<LiveStreamListing> GetStreamCandidatesAsync(long roomId, long qn, IReadOnlyList<StreamCodec> codecPriority, IReadOnlyList<StreamFormat> formatPriority, CancellationToken cancellationToken = default)
	{
		Uri uri = CreateUri($"{LiveApi}xlive/web-room/v2/index/getRoomPlayInfo?room_id={roomId}&no_playurl=0&mask=1&qn={qn}&platform=web&protocol=0,1&format=0,1,2&codec=0,1,2");
		RoomPlayInfoData data = await GetDataAsync(uri, BilibiliJsonContext.Default.RoomPlayInfoData, cancellationToken);

		if (ToLiveStatus(data.LiveStatus) is not LiveStatus.Live)
		{
			throw new BilibiliApiException($"Room {roomId} is not live.");
		}

		return StreamCandidates.Select(data, codecPriority, formatPriority);
	}

	/// <param name="qn">原画及以下的档位总会列出；高于原画的档位要用对应的 qn 请求才会列出，例如原画真彩要用 25000</param>
	/// <remarks>mid 本应是请求者的 uid，但接口不拿它核对登录，所以填 0，请求也不带登录 Cookie</remarks>
	public async Task<IReadOnlyList<LiveStreamCandidate.Variant>> GetMasterPlaylistVariantsAsync(long roomId, long qn, CancellationToken cancellationToken = default)
	{
		Uri uri = CreateUri($"{LiveApi}xlive/play-gateway/master/url?cid={roomId}&mid=0&pt=web&p2p_type=-1&net=0&free_type=0&build=0&feature=2&qn={qn}&drm_type=0&codec=0,1,2");
		using HttpRequestMessage request = new(HttpMethod.Get, uri);
		request.Options.Set(BilibiliRequestHandler.AnonymousOption, true);
		using HttpResponseMessage response = await HttpClient.SendAsync(request, cancellationToken);
		response.EnsureSuccessStatusCode();

		// 未开播的房间返回空内容
		string content = await response.Content.ReadAsStringAsync(cancellationToken);
		ApiResponse.ThrowIfError(content);
		LiveStreamCandidate.Variant[] variants = MasterPlaylistVariants.Parse(content, uri);

		return variants.Length is 0 ? throw new BilibiliApiException($"The master playlist of room {roomId} lists no usable variant.") : variants;
	}

	internal async Task<DanmuServerInfo> GetDanmuServerInfoAsync(long roomId, bool anonymous, CancellationToken cancellationToken)
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
		return (await GetCurrentUserAsync(cancellationToken))?.UserId ?? 0;
	}

	/// <returns>未登录时为空</returns>
	public async Task<CurrentUser?> GetCurrentUserAsync(CancellationToken cancellationToken = default)
	{
		ApiResponse<NavData> response = await GetNavAsync(cancellationToken);

		// 紧接着要请求弹幕 token，用这次拿到的密钥签名，而不是可能已经轮换的缓存密钥
		if (GetMixinKey(response.Data) is { } mixinKey)
		{
			_wbiKeys.Store(mixinKey);
		}

		if (response.Code is BilibiliApiException.NotLoggedInCode)
		{
			return null;
		}

		NavData data = response.GetData();
		return data.IsLogin ? new CurrentUser(data.Mid, data.Uname ?? string.Empty) : null;
	}

	public async Task<QrCodeLoginTicket> CreateQrCodeLoginAsync(CancellationToken cancellationToken = default)
	{
		QrCodeGenerateData data = await GetDataAsync(new Uri($"{PassportApi}qrcode/generate"), BilibiliJsonContext.Default.QrCodeGenerateData, cancellationToken);

		if (string.IsNullOrEmpty(data.Url) || string.IsNullOrEmpty(data.QrcodeKey))
		{
			throw new BilibiliApiException("The QR code login response is incomplete.");
		}

		return new QrCodeLoginTicket(data.Url, data.QrcodeKey);
	}

	public async Task<QrCodeLoginResult> PollQrCodeLoginAsync(string key, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrEmpty(key);

		Uri uri = new($"{PassportApi}qrcode/poll?qrcode_key={Uri.EscapeDataString(key)}&source=main-fe-header");
		using HttpResponseMessage response = await HttpClient.GetAsync(uri, cancellationToken);
		QrCodePollData data = (await ApiResponse.ReadAsync(response, BilibiliJsonContext.Default.QrCodePollData, cancellationToken)).GetData();

		return data.Code switch
		{
			0 => new QrCodeLoginResult.Succeeded(ReadCredentials(response, data.RefreshToken)),
			86038 => new QrCodeLoginResult.Expired(),
			86090 => new QrCodeLoginResult.WaitingForConfirm(),
			86101 => new QrCodeLoginResult.WaitingForScan(),
			_ => throw new BilibiliApiException(data.Message ?? $"Unknown QR code login state {data.Code}.", data.Code)
		};
	}

	public async Task<CookieRefreshInfo> GetCookieRefreshInfoAsync(string cookie, CancellationToken cancellationToken = default)
	{
		using HttpRequestMessage request = CreateRequest(HttpMethod.Get, new Uri($"{PassportApi}cookie/info?csrf={Uri.EscapeDataString(BilibiliCookie.GetCsrf(cookie))}"), cookie);
		CookieInfoData data = await GetDataAsync(request, BilibiliJsonContext.Default.CookieInfoData, cancellationToken);
		return new CookieRefreshInfo(data.Refresh, data.Timestamp);
	}

	/// <returns>旧 Cookie 在 <see cref="ConfirmCookieRefreshAsync"/> 之前仍然有效</returns>
	public async Task<LoginCredentials> RefreshCookieAsync(string cookie, string refreshToken, CookieRefreshInfo info, CancellationToken cancellationToken = default)
	{
		string csrf = BilibiliCookie.GetCsrf(cookie);
		string refreshCsrf = await GetRefreshCsrfAsync(cookie, info.Timestamp, cancellationToken);

		using HttpRequestMessage request = CreatePost(new Uri($"{PassportApi}cookie/refresh"), cookie, [new("csrf", csrf), new("refresh_csrf", refreshCsrf), new("source", "main_web"), new("refresh_token", refreshToken)]);
		using HttpResponseMessage response = await HttpClient.SendAsync(request, cancellationToken);
		CookieRefreshData data = (await ApiResponse.ReadAsync(response, BilibiliJsonContext.Default.CookieRefreshData, cancellationToken)).GetData();

		return data.RefreshToken is { Length: > 0 } ? ReadCredentials(response, data.RefreshToken) : throw new BilibiliApiException("The cookie refresh response contains no refresh token.");
	}

	/// <param name="cookie">续期得到的新 Cookie</param>
	/// <param name="previousRefreshToken">续期之前的 refresh_token</param>
	public async Task ConfirmCookieRefreshAsync(string cookie, string previousRefreshToken, CancellationToken cancellationToken = default)
	{
		using HttpRequestMessage request = CreatePost(new Uri($"{PassportApi}confirm/refresh"), cookie, [new("csrf", BilibiliCookie.GetCsrf(cookie)), new("refresh_token", previousRefreshToken)]);
		using HttpResponseMessage response = await HttpClient.SendAsync(request, cancellationToken);
		await ApiResponse.EnsureSuccessAsync(response, cancellationToken);
	}

	public async Task LogoutAsync(string cookie, CancellationToken cancellationToken = default)
	{
		using HttpRequestMessage request = CreatePost(new Uri(LogoutUrl), cookie, [new("biliCSRF", BilibiliCookie.GetCsrf(cookie))]);
		using HttpResponseMessage response = await HttpClient.SendAsync(request, cancellationToken);
		await ApiResponse.EnsureSuccessAsync(response, cancellationToken);
	}

	// 路径不对、登录失效或请求太频繁都是 404
	private async Task<string> GetRefreshCsrfAsync(string cookie, long timestamp, CancellationToken cancellationToken)
	{
		using HttpRequestMessage request = CreateRequest(HttpMethod.Get, new Uri(CorrespondUrl + CorrespondPage.CreatePath(timestamp, CorrespondKey)), cookie);
		using HttpResponseMessage response = await HttpClient.SendAsync(request, cancellationToken);
		response.EnsureSuccessStatusCode();

		return CorrespondPage.ReadRefreshCsrf(await response.Content.ReadAsStringAsync(cancellationToken)) ?? throw new BilibiliApiException("The correspond page contains no refresh_csrf.");
	}

	private static LoginCredentials ReadCredentials(HttpResponseMessage response, string? refreshToken)
	{
		string[] setCookie = response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? values) ? [.. values] : [];
		string cookie = CookieHeader.FromSetCookie(setCookie);

		return cookie.Length is 0
			? throw new BilibiliApiException("The login response sets no cookie.")
			: new LoginCredentials(cookie, refreshToken is { Length: > 0 } ? refreshToken : null, CookieHeader.GetExpires(setCookie, BilibiliCookie.SessionName));
	}

	private static HttpRequestMessage CreateRequest(HttpMethod method, Uri uri, string cookie)
	{
		HttpRequestMessage request = new(method, uri);
		request.Options.Set(BilibiliRequestHandler.CookieOption, cookie);
		return request;
	}

	private static HttpRequestMessage CreatePost(Uri uri, string cookie, IEnumerable<KeyValuePair<string, string>> form)
	{
		HttpRequestMessage request = CreateRequest(HttpMethod.Post, uri, cookie);
		request.Content = new FormUrlEncodedContent(form);
		return request;
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

		// 未登录时 nav 返回 -101，但仍带着密钥
		if (response.Code is not (0 or BilibiliApiException.NotLoggedInCode))
		{
			throw new BilibiliApiException(response.Message, response.Code);
		}

		return GetMixinKey(response.Data) ?? throw new BilibiliApiException("The nav response contains no wbi keys.", response.Code);
	}

	private static string? GetMixinKey(NavData? data)
	{
		return data?.WbiImg is { ImgUrl: { } imgUrl, SubUrl: { } subUrl } && GetKey(imgUrl) is { } imgKey && GetKey(subUrl) is { } subKey ? WbiSigner.TryGetMixinKey(imgKey, subKey) : null;

		static string? GetKey(string url)
		{
			return Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ? Path.GetFileNameWithoutExtension(uri.AbsolutePath) : null;
		}
	}
}
