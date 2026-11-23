using BilibiliLive.Models;
using BilibiliLiveRecordDownLoader.Application.Contracts.Accounts;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using BilibiliLiveRecordDownLoader.Domain.Shared.Localization;
using BilibiliLiveRecordDownLoader.ViewModels;
using BilibiliLiveRecordDownLoader.ViewModels.Dialogs;
using ReactiveUI.Primitives;
using System.Collections.Concurrent;
using System.Windows.Input;

namespace BilibiliLiveRecordDownLoader.Application.Tests.ViewModels;

public sealed class AccountViewModelTest
{
	public static IEnumerable<Func<Exception>> CheckFailures()
	{
		// HttpClient 超时抛的是 TaskCanceledException，调用方并没有取消
		yield return static () => new TaskCanceledException();
		yield return static () => new HttpRequestException();
	}

	[Test]
	[MethodDataSource(nameof(CheckFailures))]
	public async Task FailedCheckLeavesTheCheckingStateAsync(Exception failure, CancellationToken cancellationToken)
	{
		FakeAccountAppService accounts = new() { IsLoggedIn = OnThreadPool(_ => throw failure) };
		using AccountViewModel viewModel = Create(accounts);
		// 先订阅，否则异常会落到全局处理
		Task<Exception> reported = viewModel.CheckLoginCommand.ThrownExceptions.FirstAsync(cancellationToken);

		((ICommand)viewModel.CheckLoginCommand).Execute(null);

		using (Assert.Multiple())
		{
			await Assert.That(await reported).IsSameReferenceAs(failure);
			await Assert.That(viewModel.IsLoggedIn).IsFalse();
		}
	}

	[Test]
	public async Task EditedCookieIsCheckedAfterItIsSavedAsync(CancellationToken cancellationToken)
	{
		ConcurrentQueue<string> checkedCookies = new();
		FakeAccountAppService accounts = new()
		{
			Cookie = "old",
			IsLoggedIn = OnThreadPool(cookie =>
			{
				checkedCookies.Enqueue(cookie ?? string.Empty);
				return true;
			})
		};
		using AccountViewModel viewModel = Create(accounts);
		await viewModel.LoadAsync(cancellationToken);
		Task loggedIn = WhenLoggedInAsync(viewModel, true, cancellationToken);

		viewModel.Cookie = "new";
		await loggedIn;

		await Assert.That(checkedCookies).IsEquivalentTo(["new"]);
	}

	// 启动时的检查还没返回，用户就粘贴了新的 Cookie；用旧 Cookie 的那次后返回，不能盖掉新 Cookie 的结果
	[Test]
	public async Task OlderCheckFinishingLastDoesNotOverrideTheNewerResultAsync()
	{
		ConcurrentQueue<TaskCompletionSource<bool>> checks = new();
		FakeAccountAppService accounts = new()
		{
			IsLoggedIn = (_, _) =>
			{
				// 续体在 SetResult 里同步执行，返回时结果已经写入
				TaskCompletionSource<bool> check = new();
				checks.Enqueue(check);
				return check.Task;
			}
		};
		using AccountViewModel viewModel = Create(accounts);
		await viewModel.LoadAsync(CancellationToken.None);

		viewModel.RefreshLoginStatus();
		viewModel.Cookie = "new";
		TaskCompletionSource<bool>[] pending = [.. checks];
		await Assert.That(pending).Count().IsEqualTo(2);
		pending[1].SetResult(true);
		pending[0].SetResult(false);

		await Assert.That(viewModel.IsLoggedIn).IsTrue();
	}

	[Test]
	public async Task CookieReloadedAfterLogoutIsNotSavedOrCheckedAgainAsync(CancellationToken cancellationToken)
	{
		FakeAccountAppService accounts = new() { Cookie = "old", IsLoggedIn = OnThreadPool(static _ => true) };
		using AccountViewModel viewModel = Create(accounts);
		await viewModel.LoadAsync(cancellationToken);
		await viewModel.CheckLoginCommand.Execute().ToTask(cancellationToken);
		int saves = accounts.Saves;

		await viewModel.LogoutCommand.Execute().ToTask(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(viewModel.IsLoggedIn).IsFalse();
			await Assert.That(viewModel.Cookie).IsNull();
			await Assert.That(accounts.Checks).IsEqualTo(1);
			await Assert.That(accounts.Saves).IsEqualTo(saves);
		}
	}

	[Test]
	public async Task SuccessfulQrCodeLoginShowsANotificationAsync(CancellationToken cancellationToken)
	{
		FakeAccountAppService accounts = new() { IsLoggedIn = OnThreadPool(static _ => true) };
		RecordingNotifications notifications = new();
		FakeTimeProvider time = new();
		using DialogHostViewModel dialogs = new();
		using AccountViewModel viewModel = new(accounts, dialogs, notifications, time);

		Task login = viewModel.QrCodeLoginCommand.Execute().ToTask(cancellationToken);
		// 不知道轮询的定时器什么时候建好，一直推进到登录走完
		await TestWait.UntilAsync(() =>
		{
			time.Advance(QrCodeLoginViewModel.PollInterval);
			return login.IsCompleted;
		}, "the QR code login completes", cancellationToken);
		await login;

		await Assert.That(notifications.Shown).IsEquivalentTo([new ShownNotification(AppLanguage.Current.Account, AppLanguage.Current.LoginSucceeded, AppNotificationType.Success, null)]);
	}

	private static AccountViewModel Create(FakeAccountAppService accounts)
	{
		return new AccountViewModel(accounts, new DialogHostViewModel(), new RecordingNotifications(), TimeProvider.System);
	}

	// 结果在别的线程上写入
	private static Task<bool?> WhenLoggedInAsync(AccountViewModel viewModel, bool isLoggedIn, CancellationToken cancellationToken)
	{
		return viewModel.WhenChanged(static viewModel => viewModel.IsLoggedIn).Keep(value => value == isLoggedIn).FirstAsync(cancellationToken);
	}

	// 和真实的请求一样，结果在别的线程上返回；参数是检查时服务里的 Cookie
	private static Func<string?, CancellationToken, Task<bool>> OnThreadPool(Func<string?, bool> isLoggedIn)
	{
		return (cookie, cancellationToken) => Task.Run(() => isLoggedIn(cookie), cancellationToken);
	}

	private sealed class FakeAccountAppService : IAccountAppService
	{
		private int _saves;
		private int _checks;

		public required Func<string?, CancellationToken, Task<bool>> IsLoggedIn { get; init; }

		public string? Cookie { get; set; }

		public int Saves => Volatile.Read(ref _saves);

		public int Checks => Volatile.Read(ref _checks);

		public Task<string?> GetCookieAsync(CancellationToken cancellationToken = default)
		{
			return Task.FromResult(Cookie);
		}

		public Task SetCookieAsync(string? cookie, CancellationToken cancellationToken = default)
		{
			Cookie = cookie;
			Interlocked.Increment(ref _saves);
			return Task.CompletedTask;
		}

		public Task<bool> IsLoggedInAsync(CancellationToken cancellationToken = default)
		{
			Interlocked.Increment(ref _checks);
			return IsLoggedIn(Cookie, cancellationToken);
		}

		// 扫码一轮询就成功
		public Task<QrCodeLoginTicket> CreateQrCodeLoginAsync(CancellationToken cancellationToken = default)
		{
			return Task.FromResult(new QrCodeLoginTicket("https://example.com/qr", "key"));
		}

		public Task<QrCodeLoginState> PollQrCodeLoginAsync(string key, CancellationToken cancellationToken = default)
		{
			return Task.FromResult(QrCodeLoginState.Succeeded);
		}

		public Task LogoutAsync(CancellationToken cancellationToken = default)
		{
			Cookie = null;
			return Task.CompletedTask;
		}
	}
}
