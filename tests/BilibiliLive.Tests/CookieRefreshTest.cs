using BilibiliLive.Api;
using BilibiliLive.Http;
using BilibiliLive.Tests.Fakes;
using Microsoft.Extensions.Time.Testing;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

namespace BilibiliLive.Tests;

public partial class CookieRefreshTest
{
	private const string Cookie = "buvid3=B; SESSDATA=old; bili_jct=oldcsrf; DedeUserID=1";
	private const long ServerTimestamp = 1_795_000_000_000;

	[GeneratedRegex("^[0-9a-f]{256}$")]
	private static partial Regex CorrespondPathPattern { get; }

	[Test]
	public async Task RefreshAndConfirmSendTheRightCredentialsAsync(CancellationToken cancellationToken)
	{
		using RSA key = RSA.Create(1024);
		FakeHttpMessageHandler handler = new(Respond);
		BilibiliIdentity identity = new(static _ => ValueTask.FromResult(new BilibiliRequestOptions(Cookie: "SESSDATA=configured")));
		using HttpClient httpClient = new(new BilibiliRequestHandler(identity) { InnerHandler = handler });
		// 本地时钟和服务器时间不同
		BilibiliApiClient client = new(httpClient, new FakeTimeProvider()) { CorrespondKey = key.ExportSubjectPublicKeyInfoPem() };

		CookieRefreshInfo info = await client.GetCookieRefreshInfoAsync(Cookie, cancellationToken);
		LoginCredentials renewed = await client.RefreshCookieAsync(Cookie, "oldtoken", info, cancellationToken);
		string renewedCookie = BilibiliCookie.Merge(Cookie, renewed.Cookie);
		await client.ConfirmCookieRefreshAsync(renewedCookie, "oldtoken", cancellationToken);

		RecordedRequest[] requests = [.. handler.Requests];
		await Assert.That(requests).Count().IsEqualTo(4);
		string correspondPath = requests[1].Uri.AbsolutePath.Substring(ApiPaths.Correspond.Length);

		using (Assert.Multiple())
		{
			await Assert.That(HttpUtility.ParseQueryString(requests[0].Uri.Query)["csrf"]).IsEqualTo("oldcsrf");
			await Assert.That(correspondPath).Matches(CorrespondPathPattern);
			await Assert.That(Encoding.UTF8.GetString(key.Decrypt(Convert.FromHexString(correspondPath), RSAEncryptionPadding.OaepSHA256))).IsEqualTo(string.Create(CultureInfo.InvariantCulture, $"refresh_{ServerTimestamp}"));
			await Assert.That(requests[2].Content).IsEqualTo("csrf=oldcsrf&refresh_csrf=refreshcsrf&source=main_web&refresh_token=oldtoken");
			await Assert.That(requests[3].Content).IsEqualTo("csrf=newcsrf&refresh_token=oldtoken");
			await Assert.That(requests.Select(static request => request.Headers["Cookie"])).IsEquivalentTo([Cookie, Cookie, Cookie, renewedCookie], CollectionOrdering.Matching);
			await Assert.That(renewed).IsEqualTo(new LoginCredentials("SESSDATA=new; bili_jct=newcsrf; DedeUserID=1; sid=s", "newtoken", new DateTimeOffset(2027, 5, 18, 10, 0, 0, TimeSpan.Zero)));
		}
	}

	[Test]
	[Arguments(ApiPaths.CookieInfo, BilibiliApiException.NotLoggedInCode)]
	[Arguments(ApiPaths.CookieRefresh, BilibiliApiException.RefreshTokenMismatchCode)]
	public async Task RejectedLoginKeepsTheApiCodeAsync(string rejectingPath, long code, CancellationToken cancellationToken)
	{
		using ApiClient api = new(request => request.RequestUri?.AbsolutePath == rejectingPath ? Responses.Json(string.Create(CultureInfo.InvariantCulture, $$"""{"code":{{code}},"message":"rejected"}""")) : Respond(request));

		await Assert.ThrowsAsync<BilibiliApiException>(async () =>
		{
			CookieRefreshInfo info = await api.Client.GetCookieRefreshInfoAsync(Cookie, cancellationToken);
			await api.Client.RefreshCookieAsync(Cookie, "oldtoken", info, cancellationToken);
		}).And.Member(static e => e.Code, actual => actual.IsEqualTo(code));
	}

	[Test]
	public async Task CorrespondPathIsEncryptedWithTheBuiltInKeyAsync()
	{
		await Assert.That(CorrespondPage.CreatePath(ServerTimestamp, CorrespondPage.PublicKey)).Matches(CorrespondPathPattern);
	}

	private static HttpResponseMessage Respond(HttpRequestMessage request)
	{
		string path = request.RequestUri?.AbsolutePath ?? string.Empty;

		if (path.StartsWith(ApiPaths.Correspond, StringComparison.Ordinal))
		{
			return Responses.Text("""<html><body><div id="1-name">refreshcsrf</div><div id="1-code">x</div></body></html>""");
		}

		switch (path)
		{
			case ApiPaths.CookieInfo:
			{
				return Responses.Json(string.Create(CultureInfo.InvariantCulture, $$$"""{"code":0,"message":"0","ttl":1,"data":{"refresh":true,"timestamp":{{{ServerTimestamp}}}}}"""));
			}
			case ApiPaths.CookieRefresh:
			{
				HttpResponseMessage response = Responses.Json("""{"code":0,"message":"0","ttl":1,"data":{"status":0,"message":"","refresh_token":"newtoken"}}""");
				response.Headers.TryAddWithoutValidation("Set-Cookie", "SESSDATA=new; Path=/; Domain=bilibili.com; Expires=Tue, 18 May 2027 10:00:00 GMT; HttpOnly; Secure; SameSite=Lax");
				response.Headers.TryAddWithoutValidation("Set-Cookie", "bili_jct=newcsrf; Path=/; Domain=bilibili.com");
				response.Headers.TryAddWithoutValidation("Set-Cookie", "DedeUserID=1; Path=/; Domain=bilibili.com");
				response.Headers.TryAddWithoutValidation("Set-Cookie", "sid=s; Path=/; Domain=bilibili.com");
				return response;
			}
			case ApiPaths.ConfirmRefresh:
			{
				return Responses.Json("""{"code":0,"message":"0","ttl":1}""");
			}
			default:
			{
				return Responses.NotFound();
			}
		}
	}
}
