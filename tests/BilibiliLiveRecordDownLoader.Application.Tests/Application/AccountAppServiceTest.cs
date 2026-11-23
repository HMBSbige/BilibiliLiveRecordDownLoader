using BilibiliLive;
using BilibiliLiveRecordDownLoader.Application.Accounts;
using BilibiliLiveRecordDownLoader.Application.Contracts.Accounts;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using BilibiliLiveRecordDownLoader.Domain.Shared.Localization;
using System.Text;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class AccountAppServiceTest
{
	[Test]
	[Timeout(30_000)]
	public async Task RequestTimeoutIsReportedAsATimeoutAsync(CancellationToken cancellationToken)
	{
		using HttpClient httpClient = new(new HangingHandler()) { Timeout = TimeSpan.FromMilliseconds(100) };
		AccountAppService accounts = new(new BilibiliApiClient(httpClient), new FakeConfigManager());

		await Assert.ThrowsAsync<TimeoutException>(() => accounts.IsLoggedInAsync(cancellationToken)).WithMessage(AppLanguage.Current.RequestTimedOut);
	}

	[Test]
	public async Task CancellationIsNotReportedAsATimeoutAsync()
	{
		using HttpClient httpClient = new(new HangingHandler());
		AccountAppService accounts = new(new BilibiliApiClient(httpClient), new FakeConfigManager());
		using CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(100));

		await Assert.ThrowsAsync<OperationCanceledException>(() => accounts.IsLoggedInAsync(cancellation.Token));
	}

	[Test]
	[Timeout(30_000)]
	public async Task SucceededLoginSavesTheCookieAsync(CancellationToken cancellationToken)
	{
		using HttpClient httpClient = new(new LoginSucceededHandler());
		AppConfig config = AppConfig.CreateDefault();
		AccountAppService accounts = new(new BilibiliApiClient(httpClient), new FakeConfigManager(config));

		QrCodeLoginState state = await accounts.PollQrCodeLoginAsync("abc", cancellationToken);

		await Assert.That((state, config.Cookie)).IsEqualTo((QrCodeLoginState.Succeeded, "SESSDATA=s"));
	}

	// 扫码登录、退出和在账号页编辑都经这里保存，读取方只需判断 null
	[Test]
	[Timeout(30_000)]
	public async Task SavedCookieIsTrimmedAndABlankOneClearsItAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		await using AppConfigManager configManager = new(Options.Create(new AppConfigStorageOptions { FilePath = directory.Combine("config.json"), LegacyFilePath = null }), NullLogger<AppConfigManager>.Instance);
		using HttpClient httpClient = new();
		AccountAppService accounts = new(new BilibiliApiClient(httpClient), configManager);

		await accounts.SetCookieAsync(" SESSDATA=x ", cancellationToken);
		string? saved = await accounts.GetCookieAsync(cancellationToken);
		await accounts.SetCookieAsync("  ", cancellationToken);

		await Assert.That((saved, await accounts.GetCookieAsync(cancellationToken))).IsEqualTo(("SESSDATA=x", null));
	}

	private sealed class LoginSucceededHandler : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			HttpResponseMessage response = new() { Content = new StringContent("""{"code":0,"message":"0","ttl":1,"data":{"url":"","refresh_token":"r","timestamp":1,"code":0,"message":""}}""", Encoding.UTF8, "application/json") };
			response.Headers.TryAddWithoutValidation("Set-Cookie", "SESSDATA=s; Path=/; Domain=bilibili.com; HttpOnly");
			return Task.FromResult(response);
		}
	}
}
