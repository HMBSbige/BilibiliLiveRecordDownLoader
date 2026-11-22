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
		public TestClient(Func<BilibiliRequestOptions> options) : this(new BilibiliIdentity(_ => ValueTask.FromResult(options())))
		{
		}

		public TestClient(BilibiliIdentity identity)
		{
			Identity = identity;
			Client = new HttpClient(new BilibiliRequestHandler(identity) { InnerHandler = Inner });
		}

		public FakeHttpMessageHandler Inner { get; } = new(_ => Responses.Text("ok"));

		public BilibiliIdentity Identity { get; }

		public HttpClient Client { get; }

		public IReadOnlyDictionary<string, string> SingleRequestHeaders => Inner.Requests.Single().Headers;

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

		IReadOnlyDictionary<string, string> headers = test.SingleRequestHeaders;

		using (Assert.Multiple())
		{
			await Assert.That(headers["Referer"]).IsEqualTo("https://live.bilibili.com/");
			await Assert.That(headers["User-Agent"]).IsEqualTo(BilibiliRequestHandler.DefaultUserAgent);
			await Assert.That(headers["Accept"]).IsEqualTo(BilibiliRequestHandler.DefaultAccept);
			await Assert.That(headers["Cookie"]).IsEqualTo($"buvid3={test.Identity.GeneratedBuvid3}");
			await Assert.That(Buvid3Pattern().IsMatch(test.Identity.GeneratedBuvid3)).IsTrue();
		}
	}

	[Test]
	public async Task OptionsAreReadForEveryRequestAsync(CancellationToken cancellationToken)
	{
		string userAgent = "agent/1";
		string cookie = "SESSDATA=first";
		using TestClient test = new(() => new BilibiliRequestOptions(userAgent, cookie));

		await test.Client.GetAsync(Url, cancellationToken);
		userAgent = "agent/2";
		cookie = "SESSDATA=second;";
		await test.Client.GetAsync(Url, cancellationToken);

		RecordedRequest[] requests = [.. test.Inner.Requests];

		using (Assert.Multiple())
		{
			await Assert.That(requests[0].Headers["User-Agent"]).IsEqualTo("agent/1");
			await Assert.That(requests[0].Headers["Cookie"]).IsEqualTo($"SESSDATA=first; buvid3={test.Identity.GeneratedBuvid3}");
			await Assert.That(requests[1].Headers["User-Agent"]).IsEqualTo("agent/2");
			await Assert.That(requests[1].Headers["Cookie"]).IsEqualTo($"SESSDATA=second; buvid3={test.Identity.GeneratedBuvid3}");
		}
	}

	[Test]
	[Arguments("https://xy1x2x3x4xy.mcdn.bilivideo.cn:4483/live-bvc/1/live_1.flv")]
	[Arguments("https://cn-gddg-ct-01-01.bilivideo.com/live-bvc/1/live_1.flv")]
	[Arguments("https://evilbilibili.com/")]
	public async Task CookieIsNotSentOutsideBilibiliDomainsAsync(string url, CancellationToken cancellationToken)
	{
		using TestClient test = new(() => new BilibiliRequestOptions(Cookie: "SESSDATA=secret"));

		await test.Client.GetAsync(url, cancellationToken);

		await Assert.That(test.SingleRequestHeaders.ContainsKey("Cookie")).IsFalse();
	}

	[Test]
	public async Task CookieIsNotSentWhenExcludedAsync(CancellationToken cancellationToken)
	{
		BilibiliIdentity identity = new(static _ => ValueTask.FromResult(new BilibiliRequestOptions("agent/1", "SESSDATA=secret")));
		FakeHttpMessageHandler inner = new(_ => Responses.Text("ok"));
		using HttpClient client = new(new BilibiliRequestHandler(identity) { IncludeCookie = false, InnerHandler = inner });

		await client.GetAsync(Url, cancellationToken);

		IReadOnlyDictionary<string, string> headers = inner.Requests.Single().Headers;

		using (Assert.Multiple())
		{
			await Assert.That(headers.ContainsKey("Cookie")).IsFalse();
			await Assert.That(headers["User-Agent"]).IsEqualTo("agent/1");
		}
	}

	[Test]
	[Arguments(null)]
	[Arguments("  ")]
	public async Task BlankUserAgentFallsBackToDefaultAsync(string? userAgent)
	{
		await Assert.That(BilibiliRequestHandler.ResolveUserAgent(userAgent)).IsEqualTo(BilibiliRequestHandler.DefaultUserAgent);
		await Assert.That(BilibiliRequestHandler.ResolveUserAgent(" agent/1 ")).IsEqualTo("agent/1");
	}

	[Test]
	public async Task ExistingBuvid3IsKeptAsync(CancellationToken cancellationToken)
	{
		using TestClient test = new(() => new BilibiliRequestOptions(Cookie: "SESSDATA=x; buvid3=MINE"));

		await test.Client.GetAsync(Url, cancellationToken);

		await Assert.That(test.SingleRequestHeaders["Cookie"]).IsEqualTo("SESSDATA=x; buvid3=MINE");
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

		string[] firstCookies = [.. first.Inner.Requests.Select(r => r.Headers["Cookie"])];

		using (Assert.Multiple())
		{
			await Assert.That(firstCookies[0]).IsEqualTo(firstCookies[1]);
			await Assert.That(sameIdentity.SingleRequestHeaders["Cookie"]).IsEqualTo(firstCookies[0]);
			await Assert.That(otherIdentity.SingleRequestHeaders["Cookie"]).IsNotEqualTo(firstCookies[0]);
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

		await Assert.That(test.SingleRequestHeaders["Cookie"]).IsEqualTo($"buvid3={userBuvid3 ?? test.Identity.GeneratedBuvid3}");
	}

	[Test]
	public async Task RequestAcceptIsNotOverriddenAsync(CancellationToken cancellationToken)
	{
		using TestClient test = new(() => new BilibiliRequestOptions());
		using HttpRequestMessage request = new(HttpMethod.Get, Url);
		request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("video/x-flv"));

		await test.Client.SendAsync(request, cancellationToken);

		await Assert.That(test.SingleRequestHeaders["Accept"]).IsEqualTo("video/x-flv");
	}

	[Test]
	public async Task RequestCookieAndUserAgentAreReplacedAsync(CancellationToken cancellationToken)
	{
		using TestClient test = new(() => new BilibiliRequestOptions("agent/1", "buvid3=OPTION"));
		using HttpRequestMessage request = new(HttpMethod.Get, Url);
		request.Headers.TryAddWithoutValidation("Cookie", "stale=1");
		request.Headers.UserAgent.ParseAdd("stale/1");

		await test.Client.SendAsync(request, cancellationToken);

		IReadOnlyDictionary<string, string> headers = test.SingleRequestHeaders;

		using (Assert.Multiple())
		{
			await Assert.That(headers["Cookie"]).IsEqualTo("buvid3=OPTION");
			await Assert.That(headers["User-Agent"]).IsEqualTo("agent/1");
		}
	}
}
