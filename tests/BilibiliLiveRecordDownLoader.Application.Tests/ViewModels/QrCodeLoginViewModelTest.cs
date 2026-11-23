using BilibiliLive.Models;
using BilibiliLiveRecordDownLoader.Application.Contracts.Accounts;
using BilibiliLiveRecordDownLoader.ViewModels.Dialogs;

namespace BilibiliLiveRecordDownLoader.Application.Tests.ViewModels;

public sealed class QrCodeLoginViewModelTest
{
	[Test]
	[Timeout(30_000)]
	public async Task FailureToCreateTheQrCodeStopsLoadingAndShowsTheReasonAsync(CancellationToken cancellationToken)
	{
		using QrCodeLoginViewModel dialog = new(new FailingAccountAppService(new TimeoutException("请求超时")));

		dialog.OnOpened();

		await TestWait.UntilAsync(() => dialog.State is QrCodeLoginState.Expired, "the failed request shows the code as expired", cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(dialog.IsLoadingQrCode).IsFalse();
			await Assert.That(dialog.QrCodeUrl).IsNull();
			await Assert.That(dialog.ErrorMessage).IsEqualTo("请求超时");
			await Assert.That(dialog.Result.IsCompleted).IsFalse();
		}
	}

	private sealed class FailingAccountAppService(Exception failure) : IAccountAppService
	{
		public Task<string?> GetCookieAsync(CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task SetCookieAsync(string? cookie, CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task<bool> IsLoggedInAsync(CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public async Task<QrCodeLoginTicket> CreateQrCodeLoginAsync(CancellationToken cancellationToken = default)
		{
			await Task.Yield();
			throw failure;
		}

		public Task<QrCodeLoginState> PollQrCodeLoginAsync(string key, CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task LogoutAsync(CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}
	}
}
