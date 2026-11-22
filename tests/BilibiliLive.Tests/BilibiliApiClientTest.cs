using BilibiliLive.Http;
using BilibiliLive.Tests.Fakes;
using System.Net;
using System.Web;

namespace BilibiliLive.Tests;

public class BilibiliApiClientTest
{
	private static ApiClient Single(string path, Func<HttpResponseMessage> response)
	{
		return ApiClient.Routes(new Dictionary<string, Func<HttpResponseMessage>> { [path] = response });
	}

	[Test]
	public async Task GetRoomInfoResolvesShortIdAsync(CancellationToken cancellationToken)
	{
		using ApiClient api = Single(ApiPaths.RoomBaseInfo, () => Responses.Fixture("room_base_info.json"));

		LiveRoomInfo info = await api.Client.GetRoomInfoAsync(732, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(info).IsEqualTo(new LiveRoomInfo(6154037, 732, 194484313, "Asaki大人", "GTA6！满血复活！", LiveStatus.Live));
			await Assert.That(api.Handler.Requests.Single().Uri.Query).Contains("room_ids=732");
		}
	}

	[Test]
	public async Task GetRoomInfoThrowsWhenRoomIsMissingAsync(CancellationToken cancellationToken)
	{
		using ApiClient api = Single(ApiPaths.RoomBaseInfo, () => Responses.Json("""{"code":0,"message":"OK","data":{"by_uids":{},"by_room_ids":{}}}"""));

		BilibiliApiException? exception = await Assert.ThrowsAsync<BilibiliApiException>(() => api.Client.GetRoomInfoAsync(999999999, cancellationToken));

		await Assert.That(exception?.Code).IsEqualTo(60004);
	}

	[Test]
	public async Task GetRoomInfosRequestsAllRoomsAtOnceAsync(CancellationToken cancellationToken)
	{
		using ApiClient api = Single(ApiPaths.RoomBaseInfo, () => Responses.Json("""{"code":0,"message":"OK","data":{"by_room_ids":{"6154037":{"room_id":6154037,"short_id":732,"uid":194484313,"uname":"Asaki大人","title":"t","live_status":1},"5440":{"room_id":5440,"short_id":1,"uid":9617619,"live_status":0}}}}"""));

		IReadOnlyList<LiveRoomInfo> rooms = await api.Client.GetRoomInfosAsync([732, 5440, 999999999], cancellationToken);

		using (Assert.Multiple())
		{
			await Sequences.AssertEqualAsync(rooms, [new LiveRoomInfo(6154037, 732, 194484313, "Asaki大人", "t", LiveStatus.Live), new LiveRoomInfo(5440, 1, 9617619, string.Empty, string.Empty, LiveStatus.Idle)]);
			await Assert.That(api.Handler.Requests.Single().Uri.Query).Contains("room_ids=732&room_ids=5440&room_ids=999999999&");
		}
	}

	[Test]
	public async Task FollowedRoomsArePagedUntilTheLastPageAsync(CancellationToken cancellationToken)
	{
		ManualTimeProvider time = new();
		using ApiClient api = FollowingPages(time, FollowingPage(3, Room(1, "a", 1), Room(2, "b", 0)), FollowingPage(3, Room(3, "c", 2)), FollowingPage(3, Room(4, "d", 0)));

		List<FollowedLiveRoom> rooms = await ToListAsync(api.Client.GetFollowedRoomsAsync(cancellationToken));

		FollowedLiveRoom[] expected =
		[
			new(1, 101, "a", "title 1", LiveStatus.Live),
			new(2, 102, "b", "title 2", LiveStatus.Idle),
			new(3, 103, "c", "title 3", LiveStatus.Round),
			new(4, 104, "d", "title 4", LiveStatus.Idle)
		];

		using (Assert.Multiple())
		{
			await Sequences.AssertEqualAsync(rooms, expected);
			await Sequences.AssertEqualAsync(api.Handler.Requests.Select(static r => HttpUtility.ParseQueryString(r.Uri.Query)["page"]), ["1", "2", "3"]);
			await Assert.That(api.Handler.Requests.All(static r => HttpUtility.ParseQueryString(r.Uri.Query)["page_size"] is "10")).IsTrue();
			await Sequences.AssertEqualAsync(time.Delays, [BilibiliApiClient.FollowingPageInterval, BilibiliApiClient.FollowingPageInterval]);
		}
	}

	[Test]
	public async Task FollowedRoomsSkipDuplicatesAndEntriesWithoutRoomAsync(CancellationToken cancellationToken)
	{
		using ApiClient api = FollowingPages(new ManualTimeProvider(), """{"code":0,"message":"0","data":{"totalPage":2,"list":[{"roomid":1,"uid":101},{"uid":102,"uname":"no room"},{"roomid":0,"uid":103}]}}""", FollowingPage(2, Room(1, "a", 1), Room(5, "e", 0)));

		List<FollowedLiveRoom> rooms = await ToListAsync(api.Client.GetFollowedRoomsAsync(cancellationToken));

		await Sequences.AssertEqualAsync(rooms, [new FollowedLiveRoom(1, 101, string.Empty, string.Empty, LiveStatus.Idle), new FollowedLiveRoom(5, 105, "e", "title 5", LiveStatus.Idle)]);
	}

	[Test]
	[Arguments("""{"code":0,"message":"0","data":{"totalPage":0,"count":0,"list":[]}}""")]
	[Arguments("""{"code":0,"message":"0","data":{"totalPage":5}}""")]
	public async Task FollowedRoomsStopAtAnEmptyPageAsync(string json, CancellationToken cancellationToken)
	{
		using ApiClient api = FollowingPages(new ManualTimeProvider(), json, json);

		List<FollowedLiveRoom> rooms = await ToListAsync(api.Client.GetFollowedRoomsAsync(cancellationToken));

		using (Assert.Multiple())
		{
			await Assert.That(rooms).IsEmpty();
			await Assert.That(api.Handler.Requests.Count).IsEqualTo(1);
		}
	}

	[Test]
	public async Task FollowedRoomsWithoutTotalPageStopAtAPageThatAddsNothingAsync(CancellationToken cancellationToken)
	{
		using ApiClient api = FollowingPages(new ManualTimeProvider(), FollowingPage(0, Room(1, "a", 1), Room(2, "b", 0)), FollowingPage(0, Room(3, "c", 0)), FollowingPage(0, Room(3, "c", 0)), FollowingPage(0, Room(4, "d", 0)));

		List<FollowedLiveRoom> rooms = await ToListAsync(api.Client.GetFollowedRoomsAsync(cancellationToken));

		using (Assert.Multiple())
		{
			await Sequences.AssertEqualAsync(rooms.Select(static room => room.RoomId), [1L, 2L, 3L]);
			await Assert.That(api.Handler.Requests.Count).IsEqualTo(3);
		}
	}

	[Test]
	public async Task FollowedRoomsStopRequestingOnceCanceledAsync(CancellationToken cancellationToken)
	{
		using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		using ApiClient api = FollowingPages(new ManualTimeProvider(), FollowingPage(3, Room(1, "a", 1)), FollowingPage(3, Room(2, "b", 1)), FollowingPage(3, Room(3, "c", 1)));

		await Assert.ThrowsAsync<OperationCanceledException>
		(async () =>
			{
				await foreach (FollowedLiveRoom _ in api.Client.GetFollowedRoomsAsync(cancellation.Token))
				{
					await cancellation.CancelAsync();
				}
			}
		);

		await Assert.That(api.Handler.Requests.Count).IsEqualTo(1);
	}

	[Test]
	public async Task NonZeroCodeThrowsWithMessageAndCodeAsync(CancellationToken cancellationToken)
	{
		using ApiClient api = Single(ApiPaths.RoomBaseInfo, () => Responses.Json("""{"code":-400,"message":"请求错误","ttl":1,"data":[]}"""));

		BilibiliApiException? exception = await Assert.ThrowsAsync<BilibiliApiException>(() => api.Client.GetRoomInfoAsync(1, cancellationToken));

		using (Assert.Multiple())
		{
			await Assert.That(exception?.Code).IsEqualTo(-400);
			await Assert.That(exception?.Message).IsEqualTo("请求错误");
		}
	}

	[Test]
	public async Task StreamCandidatesAreFilteredAndSortedByPriorityAsync(CancellationToken cancellationToken)
	{
		using ApiClient api = Single(ApiPaths.RoomPlayInfo, () => Responses.Fixture("room_play_info.json"));

		IReadOnlyList<LiveStreamCandidate.Listed> candidates = await api.Client.GetStreamCandidatesAsync(732, 10000, [StreamCodec.Hevc, StreamCodec.Avc], [StreamFormat.Fmp4, StreamFormat.Flv], cancellationToken);

		(StreamProtocol, StreamFormat, StreamCodec)[] expected =
		[
			(StreamProtocol.HttpHls, StreamFormat.Fmp4, StreamCodec.Hevc),
			(StreamProtocol.HttpHls, StreamFormat.Ts, StreamCodec.Hevc),
			(StreamProtocol.HttpHls, StreamFormat.Fmp4, StreamCodec.Avc),
			(StreamProtocol.HttpStream, StreamFormat.Flv, StreamCodec.Avc),
			(StreamProtocol.HttpHls, StreamFormat.Ts, StreamCodec.Avc)
		];

		LiveStreamCandidate.Listed flv = candidates.Single(c => c.Format is StreamFormat.Flv);
		string query = api.Handler.Requests.Single().Uri.Query;

		using (Assert.Multiple())
		{
			await Sequences.AssertEqualAsync(candidates.Select(c => (c.Protocol, c.Format, c.Codec)), expected);
			await Assert.That(flv.Qn).IsEqualTo(10000);
			await Sequences.AssertEqualAsync(flv.AcceptQualities, [10000L, 400, 250]);
			await Sequences.AssertEqualAsync(candidates.Single(static c => c is { Format: StreamFormat.Fmp4, Codec: StreamCodec.Hevc }).AcceptQualities, [250L]);
			await Assert.That(candidates.Single(static c => c is { Format: StreamFormat.Fmp4, Codec: StreamCodec.Avc }).AcceptQualities).IsEmpty();
			await Sequences.AssertEqualAsync(flv.Urls.Select(u => u.AbsoluteUri), ["https://a.bilivideo.com/live-bvc/1/live_2500.flv?expires=1&sign=a", "https://c.bilivideo.com/live-bvc/1/live_2500.flv?expires=1&sign=c"]);
			await Assert.That(query).Contains("room_id=732");
			await Assert.That(query).Contains("qn=10000");
			await Assert.That(query).Contains("protocol=0,1&format=0,1,2&codec=0,1,2");
		}
	}

	[Test]
	public async Task StreamCandidatesKeepApiOrderWithoutPriorityAsync(CancellationToken cancellationToken)
	{
		using ApiClient api = Single(ApiPaths.RoomPlayInfo, () => Responses.Fixture("room_play_info.json"));

		IReadOnlyList<LiveStreamCandidate.Listed> candidates = await api.Client.GetStreamCandidatesAsync(732, 10000, [], [], cancellationToken);

		(StreamFormat, StreamCodec)[] expected =
		[
			(StreamFormat.Flv, StreamCodec.Avc),
			(StreamFormat.Ts, StreamCodec.Avc),
			(StreamFormat.Ts, StreamCodec.Hevc),
			(StreamFormat.Fmp4, StreamCodec.Avc),
			(StreamFormat.Fmp4, StreamCodec.Hevc)
		];

		await Sequences.AssertEqualAsync(candidates.Select(c => (c.Format, c.Codec)), expected);
	}

	[Test]
	public async Task StreamCandidatesThrowWhenNotLiveAsync(CancellationToken cancellationToken)
	{
		using ApiClient api = Single(ApiPaths.RoomPlayInfo, () => Responses.Json("""{"code":0,"message":"OK","data":{"room_id":5440,"short_id":1,"live_status":2,"playurl_info":null}}"""));

		await Assert.ThrowsAsync<BilibiliApiException>(() => api.Client.GetStreamCandidatesAsync(1, 10000, [], [], cancellationToken));
	}

	[Test]
	public async Task StreamCandidatesThrowOnApiErrorAsync(CancellationToken cancellationToken)
	{
		using ApiClient api = Single(ApiPaths.RoomPlayInfo, () => Responses.Json("""{"code":60004,"message":"房间不存在","ttl":1,"data":null}"""));

		BilibiliApiException? exception = await Assert.ThrowsAsync<BilibiliApiException>(() => api.Client.GetStreamCandidatesAsync(999999999, 10000, [], [], cancellationToken));

		await Assert.That(exception?.Code).IsEqualTo(60004);
	}

	[Test]
	public async Task MasterPlaylistIsRequestedUnsignedWithTheGivenMidAndWithoutTheLoginCookieAsync(CancellationToken cancellationToken)
	{
		BilibiliIdentity identity = new(static _ => ValueTask.FromResult(new BilibiliRequestOptions(Cookie: "SESSDATA=secret; buvid3=known")));
		using ApiClient api = new((request, _) => Task.FromResult(request.RequestUri?.AbsolutePath is ApiPaths.MasterPlaylist ? Responses.Text(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "master_playlist.m3u8"))) : Responses.NotFound()), identity: identity);

		IReadOnlyList<LiveStreamCandidate.Variant> variants = await api.Client.GetMasterPlaylistVariantsAsync(6650029, 105022844, cancellationToken);
		RecordedRequest request = api.Handler.Requests.Single();
		string? cookie = request.Headers.GetValueOrDefault("Cookie");

		using (Assert.Multiple())
		{
			await Assert.That(variants.Count).IsEqualTo(7);
			await Assert.That(request.Uri.AbsoluteUri).IsEqualTo("https://api.live.bilibili.com/xlive/play-gateway/master/url?cid=6650029&mid=105022844&pt=web&p2p_type=-1&net=0&free_type=0&build=0&feature=2&qn=10000&drm_type=0&codec=0,1");
			// mid is not the requester, so the login must not go with it; buvid3 stays the same as on other requests
			await Assert.That(CookieHeader.GetValue(cookie, "SESSDATA")).IsNull();
			await Assert.That(CookieHeader.GetValue(cookie, "buvid3")).IsEqualTo("known");
		}
	}

	[Test]
	public async Task MasterPlaylistWithoutUsableVariantsThrowsAsync(CancellationToken cancellationToken)
	{
		using ApiClient api = Single(ApiPaths.MasterPlaylist, static () => Responses.Text("#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=1,CODECS=\"vp09.00.10.08\",BILI-QN=10000\nhttps://a.test/index.m3u8\n"));

		await Assert.ThrowsAsync<BilibiliApiException>(() => api.Client.GetMasterPlaylistVariantsAsync(1, 0, cancellationToken));
	}

	[Test]
	public async Task MasterPlaylistFailsOnAnErrorStatusAsync(CancellationToken cancellationToken)
	{
		using ApiClient api = Single(ApiPaths.MasterPlaylist, static () => Responses.Status(HttpStatusCode.PreconditionFailed));

		await Assert.ThrowsAsync<HttpRequestException>(() => api.Client.GetMasterPlaylistVariantsAsync(1, 0, cancellationToken));
	}

	[Test]
	public async Task DanmuServerInfoSkipsHostsWithoutNameAsync(CancellationToken cancellationToken)
	{
		using ApiClient api = ApiClient.Routes(new Dictionary<string, Func<HttpResponseMessage>>
		{
			[ApiPaths.Nav] = () => Responses.Fixture("nav_anonymous.json"),
			[ApiPaths.DanmuInfo] = () => Responses.Fixture("danmu_info.json")
		});

		DanmuServerInfo info = await api.Client.GetDanmuServerInfoAsync(732, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(info.Token).IsEqualTo("danmu-token");
			await Sequences.AssertEqualAsync(info.Hosts, [new DanmuHost("zj-cn-live-comet.chat.bilibili.com", 2245), new DanmuHost("broadcastlv.chat.bilibili.com", 443)]);
		}
	}

	[Test]
	[Arguments("nav_logged_in.json", 12345678L, true)]
	[Arguments("nav_anonymous.json", 0L, false)]
	public async Task CurrentUserComesFromNavAsync(string fixture, long expectedUserId, bool expectedLoggedIn, CancellationToken cancellationToken)
	{
		using ApiClient api = Single(ApiPaths.Nav, () => Responses.Fixture(fixture));

		long userId = await api.Client.GetCurrentUserIdAsync(cancellationToken);
		bool loggedIn = await api.Client.IsLoggedInAsync(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(userId).IsEqualTo(expectedUserId);
			await Assert.That(loggedIn).IsEqualTo(expectedLoggedIn);
		}
	}

	[Test]
	public async Task CurrentUserThrowsOnOtherErrorsAsync(CancellationToken cancellationToken)
	{
		using ApiClient api = Single(ApiPaths.Nav, () => Responses.Json("""{"code":-412,"message":"请求被拦截"}"""));

		BilibiliApiException? exception = await Assert.ThrowsAsync<BilibiliApiException>(() => api.Client.GetCurrentUserIdAsync(cancellationToken));

		await Assert.That(exception?.Code).IsEqualTo(-412);
	}

	[Test]
	public async Task CreateQrCodeLoginReturnsUrlAndKeyAsync(CancellationToken cancellationToken)
	{
		using ApiClient api = Single(ApiPaths.QrCodeGenerate, () => Responses.Json("""{"code":0,"message":"OK","ttl":1,"data":{"url":"https://account.bilibili.com/h5/account-h5/auth/scan-web?qrcode_key=abc","qrcode_key":"abc"}}"""));

		QrCodeLoginTicket ticket = await api.Client.CreateQrCodeLoginAsync(cancellationToken);

		await Assert.That(ticket).IsEqualTo(new QrCodeLoginTicket("https://account.bilibili.com/h5/account-h5/auth/scan-web?qrcode_key=abc", "abc"));
	}

	[Test]
	[Arguments(86101, QrCodeLoginState.WaitingForScan)]
	[Arguments(86090, QrCodeLoginState.WaitingForConfirm)]
	[Arguments(86038, QrCodeLoginState.Expired)]
	public async Task PollQrCodeLoginMapsPendingStatesAsync(int code, QrCodeLoginState expected, CancellationToken cancellationToken)
	{
		using ApiClient api = Single(ApiPaths.QrCodePoll, () => Responses.Json($$$"""{"code":0,"message":"0","ttl":1,"data":{"url":"","refresh_token":"","timestamp":0,"code":{{{code}}},"message":"..."}}"""));

		QrCodeLoginResult result = await api.Client.PollQrCodeLoginAsync("abc", cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(result).IsEqualTo(new QrCodeLoginResult(expected));
			await Assert.That(api.Handler.Requests.Single().Uri.Query).Contains("qrcode_key=abc");
		}
	}

	[Test]
	public async Task PollQrCodeLoginBuildsCookieFromSetCookieAsync(CancellationToken cancellationToken)
	{
		using ApiClient api = Single(ApiPaths.QrCodePoll, () =>
		{
			HttpResponseMessage response = Responses.Json("""{"code":0,"message":"0","ttl":1,"data":{"url":"https://passport.biligame.com/x/passport-login/web/crossDomain?DedeUserID=12345678","refresh_token":"r","timestamp":1,"code":0,"message":""}}""");
			response.Headers.TryAddWithoutValidation("Set-Cookie", "SESSDATA=s%2Ce; Path=/; Domain=bilibili.com; HttpOnly; Secure");
			response.Headers.TryAddWithoutValidation("Set-Cookie", "bili_jct=csrf; Path=/; Domain=bilibili.com");
			response.Headers.TryAddWithoutValidation("Set-Cookie", "DedeUserID=12345678; Path=/; Domain=bilibili.com");
			return response;
		});

		QrCodeLoginResult result = await api.Client.PollQrCodeLoginAsync("abc", cancellationToken);

		await Assert.That(result).IsEqualTo(new QrCodeLoginResult(QrCodeLoginState.Succeeded, "SESSDATA=s%2Ce;bili_jct=csrf;DedeUserID=12345678"));
	}

	[Test]
	public async Task PollQrCodeLoginThrowsOnUnknownStateAsync(CancellationToken cancellationToken)
	{
		using ApiClient api = Single(ApiPaths.QrCodePoll, () => Responses.Json("""{"code":0,"message":"0","ttl":1,"data":{"code":12345,"message":"未知"}}"""));

		BilibiliApiException? exception = await Assert.ThrowsAsync<BilibiliApiException>(() => api.Client.PollQrCodeLoginAsync("abc", cancellationToken));

		using (Assert.Multiple())
		{
			await Assert.That(exception?.Code).IsEqualTo(12345);
			await Assert.That(exception?.Message).IsEqualTo("未知");
		}
	}

	private static ApiClient FollowingPages(TimeProvider timeProvider, params string[] pages)
	{
		return new ApiClient
		(
			request => request.RequestUri is { AbsolutePath: ApiPaths.Following } uri && int.TryParse(HttpUtility.ParseQueryString(uri.Query)["page"], out int page) && page >= 1 && page <= pages.Length
				? Responses.Json(pages[page - 1])
				: Responses.NotFound(),
			timeProvider
		);
	}

	private static string FollowingPage(int totalPage, params string[] rooms)
	{
		return $$$"""{"code":0,"message":"0","ttl":1,"data":{"title":"哔哩哔哩直播 - 我的关注","pageSize":10,"totalPage":{{{totalPage}}},"list":[{{{string.Join(',', rooms)}}}],"count":{{{rooms.Length}}}}}""";
	}

	private static string Room(long roomId, string userName, int liveStatus)
	{
		return $$"""{"roomid":{{roomId}},"uid":{{roomId + 100}},"uname":"{{userName}}","title":"title {{roomId}}","face":"https://i0.hdslb.com/face.jpg","live_status":{{liveStatus}},"record_live_time":0}""";
	}

	private static async Task<List<T>> ToListAsync<T>(IAsyncEnumerable<T> source)
	{
		List<T> items = [];

		await foreach (T item in source)
		{
			items.Add(item);
		}

		return items;
	}
}
