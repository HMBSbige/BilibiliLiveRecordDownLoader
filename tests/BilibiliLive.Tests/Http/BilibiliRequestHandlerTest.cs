using BilibiliLive.Http;
using BilibiliLive.Tests.Fakes;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace BilibiliLive.Tests.Http;

public partial class BilibiliRequestHandlerTest
{
	private const string Url = "https://api.live.bilibili.com/test";

	[GeneratedRegex("^[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}infoc$")]
	private static partial Regex Buvid3Pattern();

	private sealed class TestClient : IDisposable
	{
		public TestClient(Func<BilibiliRequestOptions> options, bool includeCookie = true) : this(new BilibiliIdentity(_ => ValueTask.FromResult(options())), includeCookie)
		{
		}

		public TestClient(BilibiliIdentity identity, bool includeCookie = true)
		{
			Identity = identity;
			Client = new HttpClient(new BilibiliRequestHandler(identity) { IncludeCookie = includeCookie, InnerHandler = Inner });
		}

		public FakeHttpMessageHandler Inner { get; } = new(_ => Responses.Text("ok"));

		public BilibiliIdentity Identity { get; }

		public HttpClient Client { get; }

		public async Task<IReadOnlyDictionary<string, string>> SingleRequestHeadersAsync()
		{
			RecordedRequest request = await Assert.That(Inner.Requests).HasSingleItem();
			return request.Headers;
		}

		public void Dispose()
		{
			Client.Dispose();
		}
	}

	[Test]
	public async Task EmptyOptionsUseDefaultsAndGenerateBuvid3Async(CancellationToken cancellationToken)
	{
		using TestClient test = new(() => new BilibiliRequestOptions());

		await test.Client.GetAsync(Url, cancellationToken);
		IReadOnlyDictionary<string, string> headers = await test.SingleRequestHeadersAsync();

		using (Assert.Multiple())
		{
			await Assert.That(headers["Referer"]).IsEqualTo("https://live.bilibili.com/");
			await Assert.That(headers["User-Agent"]).IsEqualTo(BilibiliRequestHandler.DefaultUserAgent);
			await Assert.That(headers["Accept"]).IsEqualTo(BilibiliRequestHandler.DefaultAccept);
			await Assert.That(headers["Cookie"]).IsEqualTo($"buvid3={test.Identity.GeneratedBuvid3}");
			await Assert.That(test.Identity.GeneratedBuvid3).Matches(Buvid3Pattern());
		}
	}

	[Test]
	public async Task OptionsAreReadForEveryRequestAsync(CancellationToken cancellationToken)
	{
		string userAgent = "agent/1";
		string cookie = "SESSDATA=first";
		using TestClient test = new(() => new BilibiliRequestOptions(userAgent, cookie));
		string buvid3 = test.Identity.GeneratedBuvid3;

		await test.Client.GetAsync(Url, cancellationToken);
		userAgent = "agent/2";
		cookie = "SESSDATA=second;";
		await test.Client.GetAsync(Url, cancellationToken);

		await Assert.That(test.Inner.Requests.Select(static r => (r.Headers["User-Agent"], r.Headers["Cookie"]))).IsEquivalentTo([("agent/1", $"SESSDATA=first; buvid3={buvid3}"), ("agent/2", $"SESSDATA=second; buvid3={buvid3}")], CollectionOrdering.Matching);
	}

	[Test]
	[Arguments("https://cn-gddg-ct-01-01.bilivideo.com/live-bvc/1/live_1.flv", true)]
	[Arguments("https://evilbilibili.com/", true)]
	[Arguments(Url, false)]
	public async Task CookieIsNotSentOutsideBilibiliDomainsOrWhenExcludedAsync(string url, bool includeCookie, CancellationToken cancellationToken)
	{
		using TestClient test = new(() => new BilibiliRequestOptions("agent/1", "SESSDATA=secret"), includeCookie);

		await test.Client.GetAsync(url, cancellationToken);
		IReadOnlyDictionary<string, string> headers = await test.SingleRequestHeadersAsync();

		using (Assert.Multiple())
		{
			await Assert.That(headers).DoesNotContainKey("Cookie");
			await Assert.That(headers["User-Agent"]).IsEqualTo("agent/1");
		}
	}

	[Test]
	[Arguments(null, BilibiliRequestHandler.DefaultUserAgent)]
	[Arguments("  ", BilibiliRequestHandler.DefaultUserAgent)]
	[Arguments(" agent/1 ", "agent/1")]
	public async Task UserAgentIsTrimmedAndBlankFallsBackToDefaultAsync(string? userAgent, string expected)
	{
		await Assert.That(BilibiliRequestHandler.ResolveUserAgent(userAgent)).IsEqualTo(expected);
	}

	[Test]
	public async Task Buvid3IsSharedWithinAnIdentityAndDiffersBetweenIdentitiesAsync(CancellationToken cancellationToken)
	{
		BilibiliIdentity identity = new(static _ => ValueTask.FromResult(new BilibiliRequestOptions()));
		using TestClient first = new(identity);
		using TestClient sameIdentity = new(identity);
		using TestClient otherIdentity = new(() => new BilibiliRequestOptions());

		await first.Client.GetAsync(Url, cancellationToken);
		await first.Client.GetAsync(Url, cancellationToken);
		await sameIdentity.Client.GetAsync(Url, cancellationToken);
		await otherIdentity.Client.GetAsync(Url, cancellationToken);

		string expected = $"buvid3={identity.GeneratedBuvid3}";
		IReadOnlyDictionary<string, string> sameHeaders = await sameIdentity.SingleRequestHeadersAsync();
		IReadOnlyDictionary<string, string> otherHeaders = await otherIdentity.SingleRequestHeadersAsync();

		using (Assert.Multiple())
		{
			await Assert.That(first.Inner.Requests.Select(static r => r.Headers["Cookie"])).IsEquivalentTo([expected, expected]);
			await Assert.That(sameHeaders["Cookie"]).IsEqualTo(expected);
			await Assert.That(otherHeaders["Cookie"]).IsNotEqualTo(expected);
		}
	}

	[Test]
	[Arguments("SESSDATA=secret; bili_jct=csrf", null)]
	[Arguments("SESSDATA=secret; buvid3=MINE", "MINE")]
	public async Task AnonymousRequestSendsOnlyBuvid3Async(string cookie, string? userBuvid3, CancellationToken cancellationToken)
	{
		using TestClient test = new(() => new BilibiliRequestOptions(Cookie: cookie));
		using HttpRequestMessage request = new(HttpMethod.Get, Url);
		request.Options.Set(BilibiliRequestHandler.AnonymousOption, true);

		await test.Client.SendAsync(request, cancellationToken);
		IReadOnlyDictionary<string, string> headers = await test.SingleRequestHeadersAsync();

		await Assert.That(headers["Cookie"]).IsEqualTo($"buvid3={userBuvid3 ?? test.Identity.GeneratedBuvid3}");
	}

	[Test]
	public async Task AnonymousHeaderSendsOnlyBuvid3AndIsNotForwardedAsync(CancellationToken cancellationToken)
	{
		using TestClient test = new(() => new BilibiliRequestOptions(Cookie: "SESSDATA=secret"));
		using HttpRequestMessage request = new(HttpMethod.Get, Url);
		request.Headers.TryAddWithoutValidation(BilibiliRequestHandler.AnonymousHeader, "1");

		await test.Client.SendAsync(request, cancellationToken);
		IReadOnlyDictionary<string, string> headers = await test.SingleRequestHeadersAsync();

		using (Assert.Multiple())
		{
			await Assert.That(headers["Cookie"]).IsEqualTo($"buvid3={test.Identity.GeneratedBuvid3}");
			await Assert.That(headers).DoesNotContainKey(BilibiliRequestHandler.AnonymousHeader);
		}
	}

	[Test]
	public async Task RequestAcceptIsNotOverriddenAsync(CancellationToken cancellationToken)
	{
		using TestClient test = new(() => new BilibiliRequestOptions());
		using HttpRequestMessage request = new(HttpMethod.Get, Url);
		request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("video/x-flv"));

		await test.Client.SendAsync(request, cancellationToken);
		IReadOnlyDictionary<string, string> headers = await test.SingleRequestHeadersAsync();

		await Assert.That(headers["Accept"]).IsEqualTo("video/x-flv");
	}

	// 选项里已有 buvid3 时原样保留，不追加生成的
	[Test]
	public async Task RequestCookieAndUserAgentAreReplacedAsync(CancellationToken cancellationToken)
	{
		using TestClient test = new(() => new BilibiliRequestOptions("agent/1", "SESSDATA=x; buvid3=OPTION"));
		using HttpRequestMessage request = new(HttpMethod.Get, Url);
		request.Headers.TryAddWithoutValidation("Cookie", "stale=1");
		request.Headers.UserAgent.ParseAdd("stale/1");

		await test.Client.SendAsync(request, cancellationToken);
		IReadOnlyDictionary<string, string> headers = await test.SingleRequestHeadersAsync();

		await Assert.That((headers["Cookie"], headers["User-Agent"])).IsEqualTo(("SESSDATA=x; buvid3=OPTION", "agent/1"));
	}
}
