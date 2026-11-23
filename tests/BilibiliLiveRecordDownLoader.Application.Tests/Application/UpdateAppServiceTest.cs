using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using BilibiliLiveRecordDownLoader.Application.Updates;
using BilibiliLiveRecordDownLoader.Domain.Shared.Localization;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class UpdateAppServiceTest
{
	private static UpdateAppService CreateOffline()
	{
		return new UpdateAppService(new OfflineHttpClientFactory(), NullLogger<UpdateAppService>.Instance);
	}

	[Test]
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

	private sealed class OfflineHandler : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			throw new HttpRequestException("No network.");
		}
	}
}
