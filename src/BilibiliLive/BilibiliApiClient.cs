using BilibiliLive.Api;
using BilibiliLive.Danmu;
using BilibiliLive.Http;
using BilibiliLive.Wbi;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace BilibiliLive;

public sealed class BilibiliApiClient
{
	private const string LiveApi = "https://api.live.bilibili.com/";
	private const string NavUrl = "https://api.bilibili.com/x/web-interface/nav";
	private const string PassportApi = "https://passport.bilibili.com/x/passport-login/web/qrcode/";

	private static readonly TimeSpan BeijingOffset = TimeSpan.FromHours(8);

	// page_size is documented to accept 1-10
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

	internal static TimeSpan FollowingPageInterval { get; } = TimeSpan.FromMilliseconds(300);

	public async Task<LiveRoomInfo> GetRoomInfoAsync(long roomId, CancellationToken cancellationToken = default)
	{
		IReadOnlyList<LiveRoomInfo> rooms = await GetRoomInfosAsync([roomId], cancellationToken);
		return rooms is [var room, ..] ? room : throw new BilibiliApiException($"Room {roomId} does not exist.", BilibiliApiException.RoomNotFoundCode);
	}

	// Rooms that do not exist are left out of the result
	public async Task<IReadOnlyList<LiveRoomInfo>> GetRoomInfosAsync(IReadOnlyCollection<long> roomIds, CancellationToken cancellationToken = default)
	{
		ArgumentOutOfRangeException.ThrowIfZero(roomIds.Count);

		// getRoomBaseInfo resolves short ids and is the only unsigned endpoint that also returns the streamer name
		string query = string.Join('&', roomIds.Select(static roomId => string.Create(CultureInfo.InvariantCulture, $"room_ids={roomId}")));
		RoomBaseInfoData data = await GetDataAsync(new Uri($"{LiveApi}xlive/web-room/v1/index/getRoomBaseInfo?{query}&req_biz=web_room_componet"), BilibiliJsonContext.Default.RoomBaseInfoData, cancellationToken);
		return data.ByRoomIds is { } rooms ? [.. rooms.Values.Select(ToRoomInfo)] : [];
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

	// live_time has no offset and is always Beijing time; rooms that have never been live report all zeros
	internal static DateTimeOffset? ParseBeijingTime(string? value)
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
				// Entries can shift between pages while paging, so the same room may show up twice
				if (room.RoomId > 0 && seen.Add(room.RoomId))
				{
					hasNewRoom = true;
					yield return new FollowedLiveRoom(room.RoomId, room.Uname ?? string.Empty, room.Title ?? string.Empty, ToLiveStatus(room.LiveStatus));
				}
			}

			// Without totalPage, stop at a page that adds nothing in case pages past the end repeat the last one
			if (data.List is not { Count: > 0 } || (data.TotalPage > 0 ? page >= data.TotalPage : !hasNewRoom))
			{
				yield break;
			}
		}
	}

	/// <returns>Empty when the room is live but lists no stream that can be recorded.</returns>
	public async Task<IReadOnlyList<LiveStreamCandidate.Listed>> GetStreamCandidatesAsync(long roomId, long qn, IReadOnlyList<StreamCodec> codecPriority, IReadOnlyList<StreamFormat> formatPriority, CancellationToken cancellationToken = default)
	{
		Uri uri = CreateUri($"{LiveApi}xlive/web-room/v2/index/getRoomPlayInfo?room_id={roomId}&no_playurl=0&mask=1&qn={qn}&platform=web&protocol=0,1&format=0,1,2&codec=0,1,2");
		RoomPlayInfoData data = await GetDataAsync(uri, BilibiliJsonContext.Default.RoomPlayInfoData, cancellationToken);

		if (ToLiveStatus(data.LiveStatus) is not LiveStatus.Live)
		{
			throw new BilibiliApiException($"Room {roomId} is not live.");
		}

		return StreamCandidates.Select(data, codecPriority, formatPriority);
	}

	/// <param name="mid">Meant to be the uid of the requester, but not checked against the login, which is why the request goes without the login cookie.</param>
	public async Task<IReadOnlyList<LiveStreamCandidate.Variant>> GetMasterPlaylistVariantsAsync(long roomId, long mid, CancellationToken cancellationToken = default)
	{
		Uri uri = CreateUri($"{LiveApi}xlive/play-gateway/master/url?cid={roomId}&mid={mid}&pt=web&p2p_type=-1&net=0&free_type=0&build=0&feature=2&qn=10000&drm_type=0&codec=0,1");
		using HttpRequestMessage request = new(HttpMethod.Get, uri);
		request.Options.Set(BilibiliRequestHandler.AnonymousOption, true);
		using HttpResponseMessage response = await HttpClient.SendAsync(request, cancellationToken);
		response.EnsureSuccessStatusCode();

		// A room that is not live gets an empty response
		string content = await response.Content.ReadAsStringAsync(cancellationToken);
		ThrowIfErrorEnvelope(content);
		LiveStreamCandidate.Variant[] variants = MasterPlaylistVariants.Parse(content, uri);

		return variants.Length is 0 ? throw new BilibiliApiException($"The master playlist of room {roomId} lists no usable variant.") : variants;
	}

	// Errors come as an API envelope instead of a playlist, and the code tells risk control apart from a bad request
	private static void ThrowIfErrorEnvelope(string content)
	{
		if (!content.AsSpan().TrimStart().StartsWith('{'))
		{
			return;
		}

		using JsonDocument document = JsonDocument.Parse(content);

		if (ApiResponse.ReadEnvelope(document.RootElement) is (not 0 and var code, var message))
		{
			throw new BilibiliApiException(message, code);
		}
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
		ApiResponse<NavData> response = await GetNavAsync(cancellationToken);

		// The danmu token is requested right after the uid, so it gets signed with these keys rather than cached ones that may have rotated
		if (GetMixinKey(response.Data) is { } mixinKey)
		{
			_wbiKeys.Store(mixinKey);
		}

		if (response.Code is BilibiliApiException.NotLoggedInCode)
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
			0 => new QrCodeLoginResult.Succeeded(GetLoginCookie(response)),
			86038 => new QrCodeLoginResult.Expired(),
			86090 => new QrCodeLoginResult.WaitingForConfirm(),
			86101 => new QrCodeLoginResult.WaitingForScan(),
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
