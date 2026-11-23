using BilibiliLive.Danmu;
using BilibiliLive.Http;
using BilibiliLive.Tests.Fakes;
using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using System.Web;

namespace BilibiliLive.Tests;

public class BilibiliApiClientTest
{
	private static ApiClient Single(string path, Func<HttpResponseMessage> response, BilibiliIdentity? identity = null)
	{
		return ApiClient.Routes(new Dictionary<string, Func<HttpResponseMessage>> { [path] = response }, identity: identity);
	}

	[Test]
	public async Task GetRoomInfoResolvesShortIdAsync(CancellationToken cancellationToken)
	{
		using ApiClient api = Single(ApiPaths.RoomBaseInfo, static () => Responses.Fixture("room_base_info.json"));

		LiveRoomInfo info = await api.Client.GetRoomInfoAsync(732, cancellationToken);
		RecordedRequest request = await Assert.That(api.Handler.Requests).HasSingleItem();

		using (Assert.Multiple())
		{
			await Assert.That(info).IsEqualTo(new LiveRoomInfo(6154037, 732, 194484313, "Asaki大人", "GTA6！满血复活！", LiveStatus.Live)
			{
				AreaName = "新游推荐",
				ParentAreaName = "单机游戏",
				LiveStartTime = new DateTimeOffset(2026, 11, 21, 14, 1, 8, TimeSpan.FromHours(8)),
				LiveId = "748173982890321717"
			});
			await Assert.That(request.Uri.Query).Contains("room_ids=732");
		}
	}

	[Test]
	public async Task GetRoomInfoThrowsWhenRoomIsMissingAsync(CancellationToken cancellationToken)
	{
		using ApiClient api = Single(ApiPaths.RoomBaseInfo, static () => Responses.Json("""{"code":0,"message":"OK","data":{"by_uids":{},"by_room_ids":{}}}"""));

		await Assert.ThrowsAsync<BilibiliApiException>(() => api.Client.GetRoomInfoAsync(999999999, cancellationToken)).And.Member(static e => e.Code, static code => code.IsEqualTo(60004));
	}

	[Test]
	public async Task GetRoomInfosRequestsAllRoomsAtOnceAsync(CancellationToken cancellationToken)
	{
		using ApiClient api = Single(ApiPaths.RoomBaseInfo, static () => Responses.Json("""{"code":0,"message":"OK","data":{"by_room_ids":{"6154037":{"room_id":6154037,"short_id":732,"uid":194484313,"uname":"Asaki大人","title":"t","live_status":1},"5440":{"room_id":5440,"short_id":1,"uid":9617619,"live_status":0,"live_time":"0000-00-00 00:00:00"}}}}"""));

		IReadOnlyList<LiveRoomInfo> rooms = await api.Client.GetRoomInfosAsync([732, 5440, 999999999], cancellationToken);
		RecordedRequest request = await Assert.That(api.Handler.Requests).HasSingleItem();

		using (Assert.Multiple())
		{
			await Assert.That(rooms).IsEquivalentTo([new LiveRoomInfo(6154037, 732, 194484313, "Asaki大人", "t", LiveStatus.Live), new LiveRoomInfo(5440, 1, 9617619, string.Empty, string.Empty, LiveStatus.Idle)], CollectionOrdering.Matching);
			await Assert.That(request.Uri.Query).Contains("room_ids=732&room_ids=5440&room_ids=999999999&");
		}
	}

	[Test]
	public async Task FollowedRoomsArePagedUntilTheLastPageAsync(CancellationToken cancellationToken)
	{
		ManualTimeProvider time = new();
		using ApiClient api = FollowingPages(time, FollowingPage(3, Room(1, "a", 1), Room(2, "b", 0)), FollowingPage(3, Room(3, "c", 2)), FollowingPage(3, Room(4, "d", 0)));

		List<FollowedLiveRoom> rooms = await api.Client.GetFollowedRoomsAsync(cancellationToken).ToListAsync(cancellationToken);

		FollowedLiveRoom[] expected =
		[
			new(1, "a", "title 1", LiveStatus.Live),
			new(2, "b", "title 2", LiveStatus.Idle),
			new(3, "c", "title 3", LiveStatus.Round),
			new(4, "d", "title 4", LiveStatus.Idle)
		];

		using (Assert.Multiple())
		{
			await Assert.That(rooms).IsEquivalentTo(expected, CollectionOrdering.Matching);
			await Assert.That(api.Handler.Requests.Select(static r => HttpUtility.ParseQueryString(r.Uri.Query)["page"])).IsEquivalentTo((string?[])["1", "2", "3"], CollectionOrdering.Matching);
			await Assert.That(api.Handler.Requests).All(static r => HttpUtility.ParseQueryString(r.Uri.Query)["page_size"] is "10");
			await Assert.That(time.Delays).IsEquivalentTo([BilibiliApiClient.FollowingPageInterval, BilibiliApiClient.FollowingPageInterval], CollectionOrdering.Matching);
		}
	}

	[Test]
	public async Task FollowedRoomsSkipDuplicatesAndEntriesWithoutRoomAsync(CancellationToken cancellationToken)
	{
		using ApiClient api = FollowingPages(new ManualTimeProvider(), """{"code":0,"message":"0","data":{"totalPage":2,"list":[{"roomid":1,"uid":101},{"uid":102,"uname":"no room"},{"roomid":0,"uid":103}]}}""", FollowingPage(2, Room(1, "a", 1), Room(5, "e", 0)));

		List<FollowedLiveRoom> rooms = await api.Client.GetFollowedRoomsAsync(cancellationToken).ToListAsync(cancellationToken);

		await Assert.That(rooms).IsEquivalentTo([new FollowedLiveRoom(1, string.Empty, string.Empty, LiveStatus.Idle), new FollowedLiveRoom(5, "e", "title 5", LiveStatus.Idle)], CollectionOrdering.Matching);
	}

	[Test]
	[Arguments("""{"code":0,"message":"0","data":{"totalPage":5,"list":[]}}""")]
	[Arguments("""{"code":0,"message":"0","data":{"totalPage":5}}""")]
	public async Task FollowedRoomsStopAtAnEmptyPageAsync(string json, CancellationToken cancellationToken)
	{
		using ApiClient api = FollowingPages(new ManualTimeProvider(), json, json);

		List<FollowedLiveRoom> rooms = await api.Client.GetFollowedRoomsAsync(cancellationToken).ToListAsync(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(rooms).IsEmpty();
			await Assert.That(api.Handler.Requests).Count().IsEqualTo(1);
		}
	}

	[Test]
	public async Task FollowedRoomsWithoutTotalPageStopAtAPageThatAddsNothingAsync(CancellationToken cancellationToken)
	{
		using ApiClient api = FollowingPages(new ManualTimeProvider(), FollowingPage(0, Room(1, "a", 1), Room(2, "b", 0)), FollowingPage(0, Room(3, "c", 0)), FollowingPage(0, Room(3, "c", 0)), FollowingPage(0, Room(4, "d", 0)));

		List<FollowedLiveRoom> rooms = await api.Client.GetFollowedRoomsAsync(cancellationToken).ToListAsync(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(rooms.Select(static room => room.RoomId)).IsEquivalentTo([1L, 2, 3], CollectionOrdering.Matching);
			await Assert.That(api.Handler.Requests).Count().IsEqualTo(3);
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

		await Assert.That(api.Handler.Requests).HasSingleItem();
	}

	[Test]
	public async Task NonZeroCodeThrowsWithMessageAndCodeAsync(CancellationToken cancellationToken)
	{
		using ApiClient api = Single(ApiPaths.RoomBaseInfo, static () => Responses.Json("""{"code":-400,"message":"请求错误","ttl":1,"data":[]}"""));

		await Assert.ThrowsAsync<BilibiliApiException>(() => api.Client.GetRoomInfoAsync(1, cancellationToken)).WithMessage("请求错误").And.Member(static e => e.Code, static code => code.IsEqualTo(-400));
	}

	[Test]
	public async Task StreamCandidatesAreFilteredAndSortedByPriorityAsync(CancellationToken cancellationToken)
	{
		using ApiClient api = Single(ApiPaths.RoomPlayInfo, static () => Responses.Fixture("room_play_info.json"));

		IReadOnlyList<LiveStreamCandidate.Listed> candidates = await api.Client.GetStreamCandidatesAsync(732, 10000, [StreamCodec.Hevc, StreamCodec.Avc], [StreamFormat.Fmp4, StreamFormat.Flv], cancellationToken);
		RecordedRequest request = await Assert.That(api.Handler.Requests).HasSingleItem();
		LiveStreamCandidate.Listed flv = await Assert.That(candidates).HasSingleItem(static c => c.Format is StreamFormat.Flv);
		LiveStreamCandidate.Listed fmp4Hevc = await Assert.That(candidates).HasSingleItem(static c => c is { Format: StreamFormat.Fmp4, Codec: StreamCodec.Hevc });
		LiveStreamCandidate.Listed fmp4Avc = await Assert.That(candidates).HasSingleItem(static c => c is { Format: StreamFormat.Fmp4, Codec: StreamCodec.Avc });

		(StreamProtocol, StreamFormat, StreamCodec)[] expected =
		[
			(StreamProtocol.HttpHls, StreamFormat.Fmp4, StreamCodec.Hevc),
			(StreamProtocol.HttpHls, StreamFormat.Ts, StreamCodec.Hevc),
			(StreamProtocol.HttpHls, StreamFormat.Fmp4, StreamCodec.Avc),
			(StreamProtocol.HttpStream, StreamFormat.Flv, StreamCodec.Avc),
			(StreamProtocol.HttpHls, StreamFormat.Ts, StreamCodec.Avc)
		];

		using (Assert.Multiple())
		{
			await Assert.That(candidates.Select(static c => (c.Protocol, c.Format, c.Codec))).IsEquivalentTo(expected, CollectionOrdering.Matching);
			await Assert.That(flv.Qn).IsEqualTo(10000);
			await Assert.That(flv.AcceptQualities).IsEquivalentTo([10000L, 400, 250], CollectionOrdering.Matching);
			await Assert.That(fmp4Hevc.AcceptQualities).IsEquivalentTo([250L], CollectionOrdering.Matching);
			await Assert.That(fmp4Avc.AcceptQualities).IsEmpty();
			await Assert.That(flv.Urls.Select(static u => u.AbsoluteUri)).IsEquivalentTo(["https://a.bilivideo.com/live-bvc/1/live_2500.flv?expires=1&sign=a", "https://c.bilivideo.com/live-bvc/1/live_2500.flv?expires=1&sign=c"], CollectionOrdering.Matching);
			await Assert.That(request.Uri.Query).Contains("room_id=732").And.Contains("qn=10000").And.Contains("protocol=0,1&format=0,1,2&codec=0,1,2");
		}
	}

	[Test]
	public async Task StreamCandidatesKeepApiOrderWithoutPriorityAsync(CancellationToken cancellationToken)
	{
		using ApiClient api = Single(ApiPaths.RoomPlayInfo, static () => Responses.Fixture("room_play_info.json"));

		IReadOnlyList<LiveStreamCandidate.Listed> candidates = await api.Client.GetStreamCandidatesAsync(732, 10000, [], [], cancellationToken);

		(StreamFormat, StreamCodec)[] expected =
		[
			(StreamFormat.Flv, StreamCodec.Avc),
			(StreamFormat.Ts, StreamCodec.Avc),
			(StreamFormat.Ts, StreamCodec.Hevc),
			(StreamFormat.Fmp4, StreamCodec.Avc),
			(StreamFormat.Fmp4, StreamCodec.Hevc)
		];

		await Assert.That(candidates.Select(static c => (c.Format, c.Codec))).IsEquivalentTo(expected, CollectionOrdering.Matching);
	}

	// 轮播时照样返回流地址，只有直播状态能挡住录制
	[Test]
	[Arguments(0)]
	[Arguments(2)]
	public async Task StreamCandidatesThrowWhenNotLiveAsync(int liveStatus, CancellationToken cancellationToken)
	{
		JsonNode root = JsonNode.Parse(Responses.FixtureText("room_play_info.json")) ?? throw new InvalidDataException();
		JsonNode data = root["data"] ?? throw new InvalidDataException();
		data["live_status"] = liveStatus;
		using ApiClient api = Single(ApiPaths.RoomPlayInfo, () => Responses.Json(root.ToJsonString()));

		await Assert.ThrowsAsync<BilibiliApiException>(() => api.Client.GetStreamCandidatesAsync(1, 10000, [], [], cancellationToken)).WithMessageContaining("is not live");
	}

	[Test]
	public async Task StreamCandidatesAreEmptyWhenLiveWithoutAStreamAsync(CancellationToken cancellationToken)
	{
		using ApiClient api = Single(ApiPaths.RoomPlayInfo, static () => Responses.Json("""{"code":0,"message":"OK","data":{"room_id":5440,"short_id":1,"live_status":1,"playurl_info":null}}"""));

		await Assert.That(await api.Client.GetStreamCandidatesAsync(1, 10000, [], [], cancellationToken)).IsEmpty();
	}

	[Test]
	public async Task MasterPlaylistIsRequestedUnsignedWithTheGivenMidAndWithoutTheLoginCookieAsync(CancellationToken cancellationToken)
	{
		BilibiliIdentity identity = new(static _ => ValueTask.FromResult(new BilibiliRequestOptions(Cookie: "SESSDATA=secret; buvid3=known")));
		using ApiClient api = Single(ApiPaths.MasterPlaylist, static () => Responses.Text(Responses.FixtureText("master_playlist.m3u8")), identity);

		IReadOnlyList<LiveStreamCandidate.Variant> variants = await api.Client.GetMasterPlaylistVariantsAsync(6650029, 105022844, cancellationToken);
		RecordedRequest request = await Assert.That(api.Handler.Requests).HasSingleItem();

		using (Assert.Multiple())
		{
			await Assert.That(variants).Count().IsEqualTo(7);
			await Assert.That(request.Uri.AbsoluteUri).IsEqualTo("https://api.live.bilibili.com/xlive/play-gateway/master/url?cid=6650029&mid=105022844&pt=web&p2p_type=-1&net=0&free_type=0&build=0&feature=2&qn=10000&drm_type=0&codec=0,1");
			await Assert.That(request.Headers["Cookie"]).IsEqualTo("buvid3=known");
		}
	}

	[Test]
	public async Task MasterPlaylistWithoutUsableVariantsThrowsAsync(CancellationToken cancellationToken)
	{
		using ApiClient api = Single(ApiPaths.MasterPlaylist, static () => Responses.Text("#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=1,CODECS=\"vp09.00.10.08\",BILI-QN=10000\nhttps://a.test/index.m3u8\n"));

		await Assert.ThrowsAsync<BilibiliApiException>(() => api.Client.GetMasterPlaylistVariantsAsync(1, 0, cancellationToken));
	}

	[Test]
	public async Task MasterPlaylistErrorKeepsTheApiCodeAsync(CancellationToken cancellationToken)
	{
		using ApiClient api = Single(ApiPaths.MasterPlaylist, static () => Responses.Json("""{"code":-400,"message":"请求错误","ttl":1}"""));

		await Assert.ThrowsAsync<BilibiliApiException>(() => api.Client.GetMasterPlaylistVariantsAsync(1, 0, cancellationToken)).And.Member(static e => e.Code, static code => code.IsEqualTo(-400));
	}

	[Test]
	public async Task MasterPlaylistFailsOnAnErrorStatusAsync(CancellationToken cancellationToken)
	{
		using ApiClient api = Single(ApiPaths.MasterPlaylist, static () => Responses.Status(HttpStatusCode.PreconditionFailed));

		await Assert.ThrowsAsync<HttpRequestException>(() => api.Client.GetMasterPlaylistVariantsAsync(1, 0, cancellationToken)).And.Member(static e => e.StatusCode, static status => status.IsEqualTo(HttpStatusCode.PreconditionFailed));
	}

	[Test]
	public async Task DanmuServerInfoSkipsHostsWithoutNameAsync(CancellationToken cancellationToken)
	{
		using ApiClient api = ApiClient.Routes(new Dictionary<string, Func<HttpResponseMessage>>
		{
			[ApiPaths.Nav] = static () => Responses.Fixture("nav_anonymous.json"),
			[ApiPaths.DanmuInfo] = static () => Responses.Fixture("danmu_info.json")
		});

		DanmuServerInfo info = await api.Client.GetDanmuServerInfoAsync(732, anonymous: false, cancellationToken);

		await Assert.That(info).IsEquivalentTo(new DanmuServerInfo("danmu-token", [new DanmuHost("zj-cn-live-comet.chat.bilibili.com", 2245), new DanmuHost("broadcastlv.chat.bilibili.com", 443)]));
	}

	[Test]
	[Arguments("nav_logged_in.json", 12345678L, true)]
	[Arguments("nav_anonymous.json", 0L, false)]
	public async Task CurrentUserComesFromNavAsync(string fixture, long expectedUserId, bool expectedLoggedIn, CancellationToken cancellationToken)
	{
		using ApiClient api = Single(ApiPaths.Nav, () => Responses.Fixture(fixture));

		long userId = await api.Client.GetCurrentUserIdAsync(cancellationToken);
		bool loggedIn = await api.Client.IsLoggedInAsync(cancellationToken);

		await Assert.That((userId, loggedIn)).IsEqualTo((expectedUserId, expectedLoggedIn));
	}

	[Test]
	public async Task CurrentUserThrowsOnOtherErrorsAsync(CancellationToken cancellationToken)
	{
		using ApiClient api = Single(ApiPaths.Nav, static () => Responses.Json("""{"code":-412,"message":"请求被拦截"}"""));

		await Assert.ThrowsAsync<BilibiliApiException>(() => api.Client.GetCurrentUserIdAsync(cancellationToken)).And.Member(static e => e.Code, static code => code.IsEqualTo(-412));
	}

	[Test]
	public async Task CreateQrCodeLoginReturnsUrlAndKeyAsync(CancellationToken cancellationToken)
	{
		using ApiClient api = Single(ApiPaths.QrCodeGenerate, static () => Responses.Json("""{"code":0,"message":"OK","ttl":1,"data":{"url":"https://account.bilibili.com/h5/account-h5/auth/scan-web?qrcode_key=abc","qrcode_key":"abc"}}"""));

		QrCodeLoginTicket ticket = await api.Client.CreateQrCodeLoginAsync(cancellationToken);

		await Assert.That(ticket).IsEqualTo(new QrCodeLoginTicket("https://account.bilibili.com/h5/account-h5/auth/scan-web?qrcode_key=abc", "abc"));
	}

	[Test]
	[Arguments("""{"code":0,"message":"OK","ttl":1,"data":{"url":"","qrcode_key":"abc"}}""")]
	[Arguments("""{"code":0,"message":"OK","ttl":1,"data":{"url":"https://account.bilibili.com/h5/account-h5/auth/scan-web?qrcode_key=abc"}}""")]
	public async Task CreateQrCodeLoginThrowsWhenTheResponseIsIncompleteAsync(string json, CancellationToken cancellationToken)
	{
		using ApiClient api = Single(ApiPaths.QrCodeGenerate, () => Responses.Json(json));

		await Assert.ThrowsAsync<BilibiliApiException>(() => api.Client.CreateQrCodeLoginAsync(cancellationToken));
	}

	[Test]
	[Arguments(86101, typeof(QrCodeLoginResult.WaitingForScan))]
	[Arguments(86090, typeof(QrCodeLoginResult.WaitingForConfirm))]
	[Arguments(86038, typeof(QrCodeLoginResult.Expired))]
	public async Task PollQrCodeLoginMapsPendingStatesAsync(int code, Type expected, CancellationToken cancellationToken)
	{
		using ApiClient api = Single(ApiPaths.QrCodePoll, () => Responses.Json(string.Create(CultureInfo.InvariantCulture, $$$"""{"code":0,"message":"0","ttl":1,"data":{"url":"","refresh_token":"","timestamp":0,"code":{{{code}}},"message":"..."}}""")));

		QrCodeLoginResult result = await api.Client.PollQrCodeLoginAsync("abc", cancellationToken);
		RecordedRequest request = await Assert.That(api.Handler.Requests).HasSingleItem();

		using (Assert.Multiple())
		{
			await Assert.That(result.GetType()).IsEqualTo(expected);
			await Assert.That(request.Uri.Query).Contains("qrcode_key=abc");
		}
	}

	[Test]
	public async Task PollQrCodeLoginBuildsCookieFromSetCookieAsync(CancellationToken cancellationToken)
	{
		using ApiClient api = Single(ApiPaths.QrCodePoll, static () =>
		{
			HttpResponseMessage response = Responses.Json("""{"code":0,"message":"0","ttl":1,"data":{"url":"https://passport.biligame.com/x/passport-login/web/crossDomain?DedeUserID=12345678","refresh_token":"r","timestamp":1,"code":0,"message":""}}""");
			response.Headers.TryAddWithoutValidation("Set-Cookie", "SESSDATA=s%2Ce; Path=/; Domain=bilibili.com; HttpOnly; Secure");
			response.Headers.TryAddWithoutValidation("Set-Cookie", "bili_jct=csrf; Path=/; Domain=bilibili.com");
			response.Headers.TryAddWithoutValidation("Set-Cookie", "DedeUserID=12345678; Path=/; Domain=bilibili.com");
			return response;
		});

		QrCodeLoginResult result = await api.Client.PollQrCodeLoginAsync("abc", cancellationToken);

		await Assert.That(result).IsEqualTo(new QrCodeLoginResult.Succeeded("SESSDATA=s%2Ce; bili_jct=csrf; DedeUserID=12345678"));
	}

	[Test]
	public async Task PollQrCodeLoginThrowsOnUnknownStateAsync(CancellationToken cancellationToken)
	{
		using ApiClient api = Single(ApiPaths.QrCodePoll, static () => Responses.Json("""{"code":0,"message":"0","ttl":1,"data":{"code":12345,"message":"未知"}}"""));

		await Assert.ThrowsAsync<BilibiliApiException>(() => api.Client.PollQrCodeLoginAsync("abc", cancellationToken)).WithMessage("未知").And.Member(static e => e.Code, static code => code.IsEqualTo(12345));
	}

	private static ApiClient FollowingPages(TimeProvider timeProvider, params string[] pages)
	{
		return new ApiClient
		(
			request => request.RequestUri is { AbsolutePath: ApiPaths.Following } uri && int.TryParse(HttpUtility.ParseQueryString(uri.Query)["page"], CultureInfo.InvariantCulture, out int page) && page >= 1 && page <= pages.Length
				? Responses.Json(pages[page - 1])
				: Responses.NotFound(),
			timeProvider
		);
	}

	private static string FollowingPage(int totalPage, params string[] rooms)
	{
		return string.Create(CultureInfo.InvariantCulture, $$$"""{"code":0,"message":"0","ttl":1,"data":{"title":"哔哩哔哩直播 - 我的关注","pageSize":10,"totalPage":{{{totalPage}}},"list":[{{{string.Join(',', rooms)}}}],"count":{{{rooms.Length}}}}}""");
	}

	private static string Room(long roomId, string userName, int liveStatus)
	{
		return string.Create(CultureInfo.InvariantCulture, $$"""{"roomid":{{roomId}},"uid":{{roomId + 100}},"uname":"{{userName}}","title":"title {{roomId}}","face":"https://i0.hdslb.com/face.jpg","live_status":{{liveStatus}},"record_live_time":0}""");
	}
}
