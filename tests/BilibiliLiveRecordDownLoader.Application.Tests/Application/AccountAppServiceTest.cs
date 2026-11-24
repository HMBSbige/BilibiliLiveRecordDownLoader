using BilibiliLive;
using BilibiliLiveRecordDownLoader.Application.Accounts;
using BilibiliLiveRecordDownLoader.Application.Contracts.Accounts;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using BilibiliLiveRecordDownLoader.Domain.Shared.Localization;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class AccountAppServiceTest
{
	private const string Cookie = "buvid3=B; SESSDATA=old; bili_jct=oldcsrf; DedeUserID=1";
	private const string CookieInfoPath = "/x/passport-login/web/cookie/info";
	private const string CorrespondPath = "/correspond/1/";
	private const string RefreshPath = "/x/passport-login/web/cookie/refresh";
	private const string ConfirmPath = "/x/passport-login/web/confirm/refresh";
	private const string LogoutPath = "/login/exit/v2";

	private static readonly DateTimeOffset Now = new(2026, 11, 24, 0, 0, 0, TimeSpan.Zero);
	private static readonly DateTimeOffset RenewedExpiry = new(2027, 5, 18, 10, 0, 0, TimeSpan.Zero);

	public static IEnumerable<Func<(string Path, Func<HttpResponseMessage> Response, CookieRenewal Expected)>> RenewalFailures()
	{
		yield return static () => (CookieInfoPath, static () => Json("""{"code":-101,"message":"账号未登录"}"""), CookieRenewal.LoginExpired);
		yield return static () => (RefreshPath, static () => Json("""{"code":86095,"message":"refresh_csrf 错误或 refresh_token 与 cookie 不匹配"}"""), CookieRenewal.LoginExpired);
		yield return static () => (RefreshPath, static () => Json("""{"code":-111,"message":"csrf 校验失败"}"""), CookieRenewal.Failed);
		yield return static () => (CorrespondPath, static () => new HttpResponseMessage(HttpStatusCode.NotFound), CookieRenewal.Failed);
		yield return static () => (CorrespondPath, static () => Html("<html><body></body></html>"), CookieRenewal.Failed);
		yield return static () => (CookieInfoPath, static () => throw new HttpRequestException("offline"), CookieRenewal.Failed);
	}

	[Test]
	public async Task RequestTimeoutIsReportedAsATimeoutAsync(CancellationToken cancellationToken)
	{
		using HttpClient httpClient = new(new HangingHandler());
		httpClient.Timeout = TimeSpan.FromMilliseconds(100);
		AccountAppService accounts = Create(httpClient, new FakeConfigManager());

		await Assert.ThrowsAsync<TimeoutException>(() => accounts.GetStatusAsync(cancellationToken)).WithMessage(AppLanguage.Current.RequestTimedOut);
	}

	[Test]
	public async Task CancellationIsNotReportedAsATimeoutAsync()
	{
		using HttpClient httpClient = new(new HangingHandler());
		AccountAppService accounts = Create(httpClient, new FakeConfigManager());
		using CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(100));

		await Assert.ThrowsAsync<OperationCanceledException>(() => accounts.GetStatusAsync(cancellation.Token));
	}

	[Test]
	public async Task SucceededLoginSavesTheCookieWithItsRefreshTokenAndExpiryAtOnceAsync(CancellationToken cancellationToken)
	{
		using HttpClient httpClient = new(new FakeBilibiliHandler(static _ =>
		{
			HttpResponseMessage response = Json("""{"code":0,"message":"0","ttl":1,"data":{"url":"","refresh_token":"r","timestamp":1,"code":0,"message":""}}""");
			response.Headers.TryAddWithoutValidation("Set-Cookie", "SESSDATA=s; Path=/; Domain=bilibili.com; Expires=Tue, 18 May 2027 10:00:00 GMT; HttpOnly");
			return response;
		}));
		AppConfig config = AppConfig.CreateDefault();
		FakeConfigManager configManager = new(config);
		AccountAppService accounts = Create(httpClient, configManager);

		QrCodeLoginState state = await accounts.PollQrCodeLoginAsync("abc", cancellationToken);

		await Assert.That((state, config.Cookie, config.RefreshToken, config.CookieExpiresAt, configManager.UpdateCount)).IsEqualTo((QrCodeLoginState.Succeeded, "SESSDATA=s", "r", RenewedExpiry, 1));
	}

	[Test]
	[Arguments(0)]
	[Arguments(-111)]
	public async Task RenewedCookieIsConfirmedOnlyAfterItIsSavedAsync(int confirmCode, CancellationToken cancellationToken)
	{
		AppConfig config = ScannedLogin(Now.AddDays(100));
		FakeConfigManager configManager = new(config);
		int savesBeforeConfirm = -1;
		FakeBilibiliHandler handler = new(path =>
		{
			if (path is not ConfirmPath)
			{
				return Respond(path);
			}

			savesBeforeConfirm = configManager.SaveCount;
			return Json(string.Create(CultureInfo.InvariantCulture, $$"""{"code":{{confirmCode}},"message":""}"""));
		});
		using HttpClient httpClient = new(handler);

		CookieRenewal renewal = await Create(httpClient, configManager).RenewCookieAsync(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(renewal).IsEqualTo(CookieRenewal.Renewed);
			await Assert.That(savesBeforeConfirm).IsEqualTo(1);
			await Assert.That(handler.ContentOf(ConfirmPath)).IsEqualTo("csrf=newcsrf&refresh_token=oldtoken");
			await Assert.That((config.Cookie, config.RefreshToken, config.CookieExpiresAt)).IsEqualTo(("buvid3=B; SESSDATA=new; bili_jct=newcsrf; DedeUserID=1; sid=s", "newtoken", RenewedExpiry));
		}
	}

	[Test]
	public async Task RenewedCookieIsNotConfirmedWhenSavingFailsAsync(CancellationToken cancellationToken)
	{
		FakeConfigManager configManager = new(ScannedLogin(Now.AddDays(100))) { SaveFailure = new IOException("disk full") };
		FakeBilibiliHandler handler = new(Respond);
		using HttpClient httpClient = new(handler);

		CookieRenewal renewal = await Create(httpClient, configManager).RenewCookieAsync(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(renewal).IsEqualTo(CookieRenewal.Failed);
			await Assert.That(handler.Paths).Contains(RefreshPath).And.DoesNotContain(ConfirmPath);
		}
	}

	[Test]
	[MethodDataSource(nameof(RenewalFailures))]
	public async Task OnlyRejectedLoginsDiscardTheRefreshTokenAsync(string failingPath, Func<HttpResponseMessage> failure, CookieRenewal expected, CancellationToken cancellationToken)
	{
		AppConfig config = ScannedLogin(Now.AddDays(100));
		using HttpClient httpClient = new(new FakeBilibiliHandler(path => path == failingPath ? failure() : Respond(path)));

		CookieRenewal renewal = await Create(httpClient, new FakeConfigManager(config)).RenewCookieAsync(cancellationToken);

		await Assert.That((renewal, config.RefreshToken, config.Cookie)).IsEqualTo((expected, expected is CookieRenewal.LoginExpired ? null : "oldtoken", Cookie));
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task LoginChangedDuringRenewalIsLeftAloneAsync(bool isRejected, CancellationToken cancellationToken)
	{
		AppConfig config = ScannedLogin(Now.AddDays(100));
		FakeBilibiliHandler handler = new(path =>
		{
			if (path is not CookieInfoPath)
			{
				return Respond(path);
			}

			// 续期期间重新扫码登录
			config.RefreshToken = "rescanned";
			return isRejected ? Json("""{"code":-101,"message":"账号未登录"}""") : CookieInfo(refresh: true);
		});
		using HttpClient httpClient = new(handler);

		CookieRenewal renewal = await Create(httpClient, new FakeConfigManager(config)).RenewCookieAsync(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That((renewal, config.Cookie, config.RefreshToken, config.CookieExpiresAt)).IsEqualTo((CookieRenewal.Skipped, Cookie, "rescanned", Now.AddDays(100)));
			await Assert.That(handler.Paths).DoesNotContain(ConfirmPath);
		}
	}

	[Test]
	public async Task LogoutDuringRenewalLogsOutTheRenewedSessionAsync(CancellationToken cancellationToken)
	{
		AppConfig config = ScannedLogin(Now.AddDays(100));
		TaskCompletionSource refreshSent = new(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource refreshReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);
		FakeBilibiliHandler handler = new(async path =>
		{
			if (path is RefreshPath)
			{
				refreshSent.SetResult();
				await refreshReleased.Task;
			}

			return Respond(path);
		});
		using HttpClient httpClient = new(handler);
		AccountAppService accounts = Create(httpClient, new FakeConfigManager(config));

		Task renewal = accounts.RenewCookieAsync(cancellationToken);
		await refreshSent.Task;
		Task logout = accounts.LogoutAsync(cancellationToken);
		refreshReleased.SetResult();
		await Task.WhenAll(renewal, logout);

		using (Assert.Multiple())
		{
			await Assert.That(handler.ContentOf(LogoutPath)).IsEqualTo("biliCSRF=newcsrf");
			await Assert.That((config.Cookie, config.RefreshToken, config.CookieExpiresAt)).IsEqualTo((null, null, null));
		}
	}

	[Test]
	[Arguments(3, true)]
	[Arguments(30, false)]
	public async Task CookieCloseToExpiryIsRenewedEvenIfTheServerDoesNotAskAsync(int daysLeft, bool isRenewed, CancellationToken cancellationToken)
	{
		FakeBilibiliHandler handler = new(static path => path is CookieInfoPath ? CookieInfo(refresh: false) : Respond(path));
		using HttpClient httpClient = new(handler);

		CookieRenewal renewal = await Create(httpClient, new FakeConfigManager(ScannedLogin(Now.AddDays(daysLeft)))).RenewCookieAsync(cancellationToken);

		await Assert.That((renewal, handler.Paths.Contains(RefreshPath))).IsEqualTo((isRenewed ? CookieRenewal.Renewed : CookieRenewal.Skipped, isRenewed));
	}

	[Test]
	[Arguments(false, "oldtoken")]
	[Arguments(true, null)]
	public async Task NoRequestIsSentWhenRenewalIsOffOrTheCookieWasNotScannedAsync(bool autoRenew, string? refreshToken, CancellationToken cancellationToken)
	{
		AppConfig config = ScannedLogin(Now.AddDays(3));
		config.AutoRenewCookie = autoRenew;
		config.RefreshToken = refreshToken;
		FakeBilibiliHandler handler = new(Respond);
		using HttpClient httpClient = new(handler);

		CookieRenewal renewal = await Create(httpClient, new FakeConfigManager(config)).RenewCookieAsync(cancellationToken);

		await Assert.That((renewal, handler.Paths.Count)).IsEqualTo((CookieRenewal.Skipped, 0));
	}

	[Test]
	[Arguments("buvid3=C; SESSDATA=old; bili_jct=oldcsrf; DedeUserID=1", true)]
	[Arguments("buvid3=B; SESSDATA=other; bili_jct=oldcsrf; DedeUserID=1", false)]
	public async Task EditedCookieKeepsTheRefreshTokenOnlyForTheSameSessionAsync(string cookie, bool isKept, CancellationToken cancellationToken)
	{
		AppConfig config = ScannedLogin(Now.AddDays(100));
		using HttpClient httpClient = new(new FakeBilibiliHandler(Respond));

		await Create(httpClient, new FakeConfigManager(config)).SetCookieAsync(cookie, cancellationToken);

		await Assert.That((config.Cookie, config.RefreshToken, config.CookieExpiresAt)).IsEqualTo((cookie, isKept ? "oldtoken" : null, isKept ? Now.AddDays(100) : null));
	}

	[Test]
	[Arguments("oldtoken", true)]
	[Arguments(null, false)]
	public async Task LogoutAlwaysClearsTheLoginButOnlyLogsOutScannedSessionsOnTheServerAsync(string? refreshToken, bool isLoggedOutOnServer, CancellationToken cancellationToken)
	{
		AppConfig config = ScannedLogin(Now.AddDays(100));
		config.RefreshToken = refreshToken;
		// Cookie 已经失效时退出接口返回登录页
		FakeBilibiliHandler handler = new(static _ => Html("<!DOCTYPE html><html><body>登录</body></html>"));
		using HttpClient httpClient = new(handler);

		await Create(httpClient, new FakeConfigManager(config)).LogoutAsync(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That((config.Cookie, config.RefreshToken, config.CookieExpiresAt)).IsEqualTo((null, null, null));
			await Assert.That(handler.ContentOf(LogoutPath)).IsEqualTo(isLoggedOutOnServer ? "biliCSRF=oldcsrf" : null);
		}
	}

	private static AccountAppService Create(HttpClient httpClient, FakeConfigManager configManager)
	{
		return new AccountAppService(new BilibiliApiClient(httpClient), configManager, new FakeTimeProvider(Now), NullLogger<AccountAppService>.Instance);
	}

	private static AppConfig ScannedLogin(DateTimeOffset expiresAt)
	{
		AppConfig config = AppConfig.CreateDefault();
		config.Cookie = Cookie;
		config.RefreshToken = "oldtoken";
		config.CookieExpiresAt = expiresAt;
		return config;
	}

	private static HttpResponseMessage Respond(string path)
	{
		switch (path)
		{
			case CookieInfoPath:
			{
				return CookieInfo(refresh: true);
			}
			case CorrespondPath:
			{
				return Html("""<div id="1-name">refreshcsrf</div>""");
			}
			case RefreshPath:
			{
				HttpResponseMessage response = Json("""{"code":0,"message":"0","ttl":1,"data":{"status":0,"message":"","refresh_token":"newtoken"}}""");
				response.Headers.TryAddWithoutValidation("Set-Cookie", "SESSDATA=new; Path=/; Domain=bilibili.com; Expires=Tue, 18 May 2027 10:00:00 GMT; HttpOnly; Secure");
				response.Headers.TryAddWithoutValidation("Set-Cookie", "bili_jct=newcsrf; Path=/; Domain=bilibili.com");
				response.Headers.TryAddWithoutValidation("Set-Cookie", "DedeUserID=1; Path=/; Domain=bilibili.com");
				response.Headers.TryAddWithoutValidation("Set-Cookie", "sid=s; Path=/; Domain=bilibili.com");
				return response;
			}
			case ConfirmPath:
			{
				return Json("""{"code":0,"message":"0","ttl":1}""");
			}
			default:
			{
				return new HttpResponseMessage(HttpStatusCode.NotFound);
			}
		}
	}

	private static HttpResponseMessage CookieInfo(bool refresh)
	{
		return Json(refresh ? """{"code":0,"data":{"refresh":true,"timestamp":1795000000000}}""" : """{"code":0,"data":{"refresh":false,"timestamp":1795000000000}}""");
	}

	private static HttpResponseMessage Json(string json)
	{
		return new HttpResponseMessage { Content = new StringContent(json, Encoding.UTF8, "application/json") };
	}

	private static HttpResponseMessage Html(string html)
	{
		return new HttpResponseMessage { Content = new StringContent(html, Encoding.UTF8, "text/html") };
	}

	// correspond 的路径每次都不同，按前缀归到一起
	private sealed class FakeBilibiliHandler(Func<string, ValueTask<HttpResponseMessage>> respond) : HttpMessageHandler
	{
		private readonly ConcurrentQueue<(string Path, string? Content)> _requests = new();

		public FakeBilibiliHandler(Func<string, HttpResponseMessage> respond) : this(path => ValueTask.FromResult(respond(path)))
		{
		}

		public IReadOnlyList<string> Paths => [.. _requests.Select(static request => request.Path)];

		public string? ContentOf(string path)
		{
			return _requests.FirstOrDefault(request => request.Path == path).Content;
		}

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			string path = request.RequestUri?.AbsolutePath ?? string.Empty;
			path = path.StartsWith(CorrespondPath, StringComparison.Ordinal) ? CorrespondPath : path;
			_requests.Enqueue((path, request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
			return await respond(path);
		}
	}
}
