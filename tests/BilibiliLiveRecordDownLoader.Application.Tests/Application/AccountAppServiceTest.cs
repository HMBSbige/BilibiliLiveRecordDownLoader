using BilibiliLive;
using BilibiliLiveRecordDownLoader.Application.Accounts;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using BilibiliLiveRecordDownLoader.Domain.Shared.Localization;
using System.Diagnostics;
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

		TimeoutException? exception = await Assert.ThrowsAsync<TimeoutException>(() => accounts.IsLoggedInAsync(cancellationToken));

		await Assert.That(exception?.Message).IsEqualTo(AppLanguage.Current.RequestTimedOut);
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

	private sealed class HangingHandler : HttpMessageHandler
	{
		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			await Task.Delay(Timeout.Infinite, cancellationToken);
			throw new UnreachableException();
		}
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
