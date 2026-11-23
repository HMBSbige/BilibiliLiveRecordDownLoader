using BilibiliLive.Models;
using BilibiliLiveRecordDownLoader.Application.Contracts.Accounts;
using BilibiliLiveRecordDownLoader.ViewModels.Dialogs;
using ReactiveUI.Primitives;
using System.Globalization;
using System.Threading.Channels;

namespace BilibiliLiveRecordDownLoader.Application.Tests.ViewModels;

public sealed class QrCodeLoginViewModelTest
{
	private static readonly Func<QrCodeLoginState> NetworkError = static () => throw new HttpRequestException();

	[Test]
	public async Task FailureToCreateTheQrCodeStopsLoadingAndShowsTheReasonAsync()
	{
		ScriptedAccountAppService accounts = new(Task.FromException<QrCodeLoginTicket>(new TimeoutException("请求超时")), []);
		using QrCodeLoginViewModel dialog = new(accounts, new FakeTimeProvider());

		dialog.OnOpened();

		using (Assert.Multiple())
		{
			await Assert.That(dialog.State).IsEqualTo(QrCodeLoginState.Expired);
			await Assert.That(dialog.IsLoadingQrCode).IsFalse();
			await Assert.That(dialog.QrCodeUrl).IsNull();
			await Assert.That(dialog.ErrorMessage).IsEqualTo("请求超时");
			await Assert.That(dialog.Result.IsCompleted).IsFalse();
		}
	}

	// 偶发的失败不打断扫码；成功轮询一次后重新计数
	[Test]
	public async Task PollFailuresBelowTheLimitAreRetriedUntilTheLoginSucceedsAsync(CancellationToken cancellationToken)
	{
		const int tolerated = QrCodeLoginViewModel.MaxConsecutivePollFailures - 1;
		ScriptedAccountAppService accounts = new
		([
			static () => throw new TimeoutException(),
			.. Enumerable.Repeat(NetworkError, tolerated - 1),
			static () => QrCodeLoginState.WaitingForConfirm,
			.. Enumerable.Repeat(NetworkError, tolerated),
			static () => QrCodeLoginState.Succeeded
		]);
		FakeTimeProvider time = new();
		using QrCodeLoginViewModel dialog = new(accounts, time);

		dialog.OnOpened();
		await accounts.PollAllAsync(time, cancellationToken);

		await Assert.That(await dialog.Result.WaitAsync(cancellationToken)).IsTrue();
	}

	[Test]
	public async Task TooManyConsecutivePollFailuresShowTheCodeAsExpiredAsync(CancellationToken cancellationToken)
	{
		ScriptedAccountAppService accounts = new([.. Enumerable.Range(1, QrCodeLoginViewModel.MaxConsecutivePollFailures).Select(static attempt => (Func<QrCodeLoginState>)(() => throw new HttpRequestException(attempt.ToString(CultureInfo.InvariantCulture))))]);
		FakeTimeProvider time = new();
		using QrCodeLoginViewModel dialog = new(accounts, time);
		Task<QrCodeLoginState> expired = dialog.WhenChanged(static dialog => dialog.State).Keep(static state => state is QrCodeLoginState.Expired).FirstAsync(cancellationToken);

		dialog.OnOpened();
		await accounts.PollAllAsync(time, cancellationToken);
		await expired;

		using (Assert.Multiple())
		{
			await Assert.That(dialog.ErrorMessage).IsEqualTo(QrCodeLoginViewModel.MaxConsecutivePollFailures.ToString(CultureInfo.InvariantCulture));
			await Assert.That(dialog.Result.IsCompleted).IsFalse();
		}
	}

	private sealed class ScriptedAccountAppService(Task<QrCodeLoginTicket> ticket, IReadOnlyList<Func<QrCodeLoginState>> polls) : IAccountAppService
	{
		private readonly Channel<int> _polled = Channel.CreateUnbounded<int>();
		private int _next;

		// 二维码同步给出，打开对话框后轮询的定时器就已建好，之后推进时间不会落空
		public ScriptedAccountAppService(IReadOnlyList<Func<QrCodeLoginState>> polls) : this(Task.FromResult(new QrCodeLoginTicket("https://example.com/qr", "key")), polls)
		{
		}

		// 定时器在没人等待时触发的那一下会留给下一次等待，所以不必等对话框回到等待处再推进；对话框提前停止轮询时尽快失败
		public async Task PollAllAsync(FakeTimeProvider time, CancellationToken cancellationToken)
		{
			for (int index = 0; index < polls.Count; ++index)
			{
				time.Advance(QrCodeLoginViewModel.PollInterval);
				await _polled.Reader.ReadAsync(cancellationToken).AsTask().WaitAsync(TestWait.DefaultTimeout, TimeProvider.System, cancellationToken);
			}
		}

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

		public Task<QrCodeLoginTicket> CreateQrCodeLoginAsync(CancellationToken cancellationToken = default)
		{
			return ticket;
		}

		public async Task<QrCodeLoginState> PollQrCodeLoginAsync(string key, CancellationToken cancellationToken = default)
		{
			Func<QrCodeLoginState> poll = polls[_next];
			_polled.Writer.TryWrite(++_next);
			await Task.Yield();
			return poll();
		}

		public Task LogoutAsync(CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}
	}
}
