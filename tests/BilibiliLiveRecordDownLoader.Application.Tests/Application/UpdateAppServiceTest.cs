using BilibiliLiveRecordDownLoader.Application.Updates;
using BilibiliLiveRecordDownLoader.Domain.Shared.Localization;
using System.Diagnostics;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class UpdateAppServiceTest
{
	private static UpdateAppService CreateOffline()
	{
		return new UpdateAppService(new OfflineHttpClientFactory(), NullLogger<UpdateAppService>.Instance);
	}

	[Test]
	public async Task ManualCheckFailureIsThrownAsync(CancellationToken cancellationToken)
	{
		await Assert.ThrowsAsync<HttpRequestException>(() => CreateOffline().CheckForUpdateAsync(false, cancellationToken)).WithMessage("No network.");
	}

	// HttpClient 超时抛的是取消异常，会被当成用户取消
	[Test]
	[Timeout(30_000)]
	public async Task ManualCheckTimeoutIsThrownAsATimeoutAsync(CancellationToken cancellationToken)
	{
		UpdateAppService updates = new(new HangingHttpClientFactory(), NullLogger<UpdateAppService>.Instance);

		await Assert.ThrowsAsync<TimeoutException>(() => updates.CheckForUpdateAsync(false, cancellationToken)).WithMessage(AppLanguage.Current.RequestTimedOut);
	}

	[Test]
	public async Task BackgroundCheckFailureReturnsNullAsync(CancellationToken cancellationToken)
	{
		await Assert.That(await CreateOffline().TryCheckForUpdateAsync(false, cancellationToken)).IsNull();
	}

	[Test]
	public async Task BackgroundCheckIsStillCancellableAsync()
	{
		using CancellationTokenSource cancelled = new();
		await cancelled.CancelAsync();

		await Assert.ThrowsAsync<OperationCanceledException>(() => CreateOffline().TryCheckForUpdateAsync(false, cancelled.Token));
	}

	private sealed class OfflineHttpClientFactory : IHttpClientFactory
	{
		public HttpClient CreateClient(string name)
		{
			return new HttpClient(new OfflineHandler());
		}
	}

	private sealed class HangingHttpClientFactory : IHttpClientFactory
	{
		public HttpClient CreateClient(string name)
		{
			return new HttpClient(new HangingHandler()) { Timeout = TimeSpan.FromMilliseconds(100) };
		}
	}

	private sealed class HangingHandler : HttpMessageHandler
	{
		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			await Task.Delay(Timeout.Infinite, cancellationToken);
			throw new UnreachableException();
		}
	}

	private sealed class OfflineHandler : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			throw new HttpRequestException("No network.");
		}
	}
}
