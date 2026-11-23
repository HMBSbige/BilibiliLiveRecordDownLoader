using BilibiliLive.Danmu;
using BilibiliLive.Tests.Fakes;
using BilibiliLive.Wbi;

namespace BilibiliLive.Tests.Wbi;

public class WbiKeyCacheTest
{
	private static ApiClient Create(ManualTimeProvider time, Func<HttpResponseMessage>? nav = null)
	{
		return ApiClient.Routes(new Dictionary<string, Func<HttpResponseMessage>>
		{
			[ApiPaths.Nav] = nav ?? (() => Responses.Fixture("nav_anonymous.json")),
			[ApiPaths.DanmuInfo] = () => Responses.Fixture("danmu_info.json")
		}, time);
	}

	private static string SignedDanmuInfoQuery(string imgKey, string subKey, TimeProvider time)
	{
		return $"?{WbiSigner.Sign([new("id", "732"), new("type", "0"), new("web_location", "444.8")], WbiSignerTest.MixinKey(imgKey, subKey), time.GetUtcNow().ToUnixTimeSeconds())}";
	}

	[Test]
	public async Task SignedRequestCarriesTimestampAndSignatureAsync(CancellationToken cancellationToken)
	{
		ManualTimeProvider time = new();
		using ApiClient api = Create(time);

		await api.Client.GetDanmuServerInfoAsync(732, anonymous: false, cancellationToken);
		RecordedRequest request = await Assert.That(api.Handler.RequestsTo(ApiPaths.DanmuInfo)).HasSingleItem();

		await Assert.That(request.Uri.Query).IsEqualTo(SignedDanmuInfoQuery(WbiSignerTest.ImgKey, WbiSignerTest.SubKey, time));
	}

	[Test]
	public async Task KeysAreCachedUntilRefreshIntervalAsync(CancellationToken cancellationToken)
	{
		ManualTimeProvider time = new();
		using ApiClient api = Create(time);

		await api.Client.GetDanmuServerInfoAsync(732, anonymous: false, cancellationToken);
		time.Advance(WbiKeyCache.RefreshInterval - TimeSpan.FromSeconds(1));
		await api.Client.GetDanmuServerInfoAsync(732, anonymous: false, cancellationToken);
		int navBeforeExpiry = api.Handler.RequestsTo(ApiPaths.Nav).Count();

		time.Advance(TimeSpan.FromSeconds(1));
		await api.Client.GetDanmuServerInfoAsync(732, anonymous: false, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(navBeforeExpiry).IsEqualTo(1);
			await Assert.That(api.Handler.RequestsTo(ApiPaths.Nav)).Count().IsEqualTo(2);
		}
	}

	[Test]
	public async Task ClockSetBackBeforeTheFetchRefreshesTheKeysAsync(CancellationToken cancellationToken)
	{
		ManualTimeProvider time = new();
		using ApiClient api = Create(time);

		await api.Client.GetDanmuServerInfoAsync(732, anonymous: false, cancellationToken);
		time.Advance(-TimeSpan.FromDays(1));
		await api.Client.GetDanmuServerInfoAsync(732, anonymous: false, cancellationToken);

		await Assert.That(api.Handler.RequestsTo(ApiPaths.Nav)).Count().IsEqualTo(2);
	}

	[Test]
	public async Task KeysOfTheUserIdRequestSignTheNextRequestAsync(CancellationToken cancellationToken)
	{
		ManualTimeProvider time = new();
		int navCalls = 0;
		using ApiClient api = Create(time, () => ++navCalls is 1 ? Responses.Fixture("nav_anonymous.json") : Nav(WbiSignerTest.SubKey, WbiSignerTest.ImgKey));

		await api.Client.GetDanmuServerInfoAsync(732, anonymous: false, cancellationToken);
		await api.Client.GetCurrentUserIdAsync(cancellationToken);
		await api.Client.GetDanmuServerInfoAsync(732, anonymous: false, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(api.Handler.RequestsTo(ApiPaths.DanmuInfo).Last().Uri.Query).IsEqualTo(SignedDanmuInfoQuery(WbiSignerTest.SubKey, WbiSignerTest.ImgKey, time));
			await Assert.That(navCalls).IsEqualTo(2);
		}
	}

	[Test]
	[Timeout(10_000)]
	public async Task ConcurrentRequestsFetchKeysOnceAsync(CancellationToken cancellationToken)
	{
		TaskCompletionSource navGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
		using ApiClient api = new(async (request, ct) =>
		{
			if (request.RequestUri?.AbsolutePath is ApiPaths.Nav)
			{
				await navGate.Task.WaitAsync(ct);
				return Responses.Fixture("nav_anonymous.json");
			}

			return Responses.Fixture("danmu_info.json");
		}, new ManualTimeProvider());

		Task[] calls = [.. Enumerable.Range(0, 8).Select(_ => api.Client.GetDanmuServerInfoAsync(732, anonymous: false, cancellationToken))];

		while (api.Handler.Requests.IsEmpty)
		{
			await Task.Delay(10, cancellationToken);
		}

		navGate.SetResult();
		await Task.WhenAll(calls);

		using (Assert.Multiple())
		{
			await Assert.That(api.Handler.RequestsTo(ApiPaths.Nav)).Count().IsEqualTo(1);
			await Assert.That(api.Handler.RequestsTo(ApiPaths.DanmuInfo)).Count().IsEqualTo(8);
		}
	}

	[Test]
	public async Task FailedFetchIsNotCachedAsync(CancellationToken cancellationToken)
	{
		int navCalls = 0;
		using ApiClient api = Create(new ManualTimeProvider(), () => ++navCalls is 1 ? Responses.Json("""{"code":-412,"message":"请求被拦截"}""") : Responses.Fixture("nav_anonymous.json"));

		await Assert.ThrowsAsync<BilibiliApiException>(() => api.Client.GetDanmuServerInfoAsync(732, anonymous: false, cancellationToken)).And.Member(static e => e.Code, static code => code.IsEqualTo(-412));
		DanmuServerInfo info = await api.Client.GetDanmuServerInfoAsync(732, anonymous: false, cancellationToken);

		await Assert.That((info.Token, navCalls)).IsEqualTo(("danmu-token", 2));
	}

	private static HttpResponseMessage Nav(string imgKey, string subKey)
	{
		return Responses.Json($$$"""{"code":-101,"data":{"isLogin":false,"wbi_img":{"img_url":"https://i0.hdslb.com/bfs/wbi/{{{imgKey}}}.png","sub_url":"https://i0.hdslb.com/bfs/wbi/{{{subKey}}}.png"}},"message":"账号未登录","ttl":1}""");
	}
}
