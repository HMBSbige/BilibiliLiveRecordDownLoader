using BilibiliLive.Models;
using BilibiliLiveRecordDownLoader.Application.Contracts.Accounts;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using BilibiliLiveRecordDownLoader.Domain.Shared.Localization;
using BilibiliLiveRecordDownLoader.ViewModels;
using BilibiliLiveRecordDownLoader.ViewModels.Dialogs;
using ReactiveUI.Primitives;
using System.Collections.Concurrent;
using System.Globalization;
using System.Windows.Input;

namespace BilibiliLiveRecordDownLoader.Application.Tests.ViewModels;

public sealed class AccountViewModelTest
{
	public static IEnumerable<Func<Exception>> CheckFailures()
	{
		yield return static () => new TaskCanceledException();
		yield return static () => new HttpRequestException();
	}

	[Test]
	[MethodDataSource(nameof(CheckFailures))]
	public async Task FailedCheckLeavesTheCheckingStateAsync(Exception failure, CancellationToken cancellationToken)
	{
		FakeAccountAppService accounts = new() { IsLoggedIn = OnThreadPool(_ => throw failure) };
		using AccountViewModel viewModel = Create(accounts);
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

		viewModel.StartAutoCheck();
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
	public async Task RenewedCookieIsReloadedWithoutBeingSavedBackAsync(CancellationToken cancellationToken)
	{
		FakeAccountAppService accounts = new() { Cookie = "old", IsLoggedIn = OnThreadPool(static _ => true) };
		accounts.Renew = () =>
		{
			accounts.Cookie = "renewed";
			return CookieRenewal.Renewed;
		};
		using AccountViewModel viewModel = Create(accounts);
		await viewModel.LoadAsync(cancellationToken);

		await viewModel.CheckLoginCommand.Execute().ToTask(cancellationToken);

		await Assert.That((viewModel.Cookie, accounts.Saves)).IsEqualTo(("renewed", 0));
	}

	[Test]
	public async Task AutoCheckRenewsAtStartupAndEveryHourAndRemindsOnceAsync(CancellationToken cancellationToken)
	{
		FakeTimeProvider time = new(new DateTimeOffset(2026, 11, 24, 0, 0, 0, TimeSpan.Zero));
		DateTimeOffset expiresAt = time.GetUtcNow().AddDays(3);
		FakeAccountAppService accounts = new() { IsLoggedIn = static (_, _) => Task.FromResult(true), ExpiresAt = expiresAt };
		RecordingNotifications notifications = new();
		using DialogHostViewModel dialogs = new();
		using AccountViewModel viewModel = new(accounts, new SettingsAppService(new FakeConfigManager()), dialogs, notifications, time);

		viewModel.StartAutoCheck();
		await TestWait.UntilAsync(() => accounts.Renewals == 1 && viewModel.IsLoggedIn is true, "the startup check finishes", cancellationToken);
		time.Advance(AccountViewModel.AutoCheckInterval);
		await TestWait.UntilAsync(() => accounts.Renewals == 2, "the first hourly check runs", cancellationToken);
		time.Advance(AccountViewModel.AutoCheckInterval);
		await TestWait.UntilAsync(() => accounts.Renewals == 3, "the second hourly check runs", cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(viewModel.IsExpiringSoon).IsTrue();
			await Assert.That(notifications.Shown.Select(static shown => shown.Message)).IsEquivalentTo([AppLanguage.Current.LoginExpiringSoon.ToString(expiresAt.ToString("G", CultureInfo.CurrentCulture))]);
		}
	}

	[Test]
	[Arguments(CookieRenewal.Skipped, 1, false)]
	[Arguments(CookieRenewal.Failed, 1, false)]
	[Arguments(CookieRenewal.Renewed, 2, false)]
	[Arguments(CookieRenewal.LoginExpired, 2, true)]
	public async Task HourlyCheckQueriesTheStatusOnlyWhenTheLoginMayHaveChangedAsync(CookieRenewal hourly, int checks, bool isExpiredNotified, CancellationToken cancellationToken)
	{
		FakeTimeProvider time = new();
		FakeAccountAppService accounts = new() { IsLoggedIn = static (_, _) => Task.FromResult(true) };
		RecordingNotifications notifications = new();
		using DialogHostViewModel dialogs = new();
		using AccountViewModel viewModel = new(accounts, new SettingsAppService(new FakeConfigManager()), dialogs, notifications, time);

		viewModel.StartAutoCheck();
		await TestWait.UntilAsync(() => accounts.Checks == 1 && viewModel.IsLoggedIn is true, "the startup check finishes", cancellationToken);
		accounts.Renew = () =>
		{
			accounts.Renew = static () => CookieRenewal.Skipped;
			return hourly;
		};
		time.Advance(AccountViewModel.AutoCheckInterval);
		await TestWait.UntilAsync(() => accounts.Renewals == 2, "the first hourly check runs", cancellationToken);
		time.Advance(AccountViewModel.AutoCheckInterval);
		// 循环串行执行，下一小时的续期开始时上一小时的处理已经结束
		await TestWait.UntilAsync(() => accounts.Renewals == 3, "the second hourly check runs", cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(accounts.Checks).IsEqualTo(checks);
			await Assert.That(notifications.Shown.Any(static shown => shown.Message == AppLanguage.Current.LoginExpired)).IsEqualTo(isExpiredNotified);
		}
	}

	[Test]
	[Arguments(false, true, 100, true)]
	[Arguments(true, true, 100, false)]
	[Arguments(true, false, 100, true)]
	[Arguments(true, true, 3, true)]
	public async Task QrCodeLoginIsOfferedWhenTheLoginCannotLastAsync(bool isLoggedIn, bool isRenewable, int daysLeft, bool offered, CancellationToken cancellationToken)
	{
		FakeAccountAppService accounts = new() { IsLoggedIn = (_, _) => Task.FromResult(isLoggedIn), IsRenewable = isRenewable, ExpiresAt = DateTimeOffset.UtcNow.AddDays(daysLeft) };
		using AccountViewModel viewModel = Create(accounts);

		await viewModel.CheckLoginCommand.Execute().ToTask(cancellationToken);

		await Assert.That(await viewModel.OffersQrCodeLogin.FirstAsync(cancellationToken)).IsEqualTo(offered);
	}

	[Test]
	public async Task SuccessfulQrCodeLoginShowsANotificationAsync(CancellationToken cancellationToken)
	{
		FakeAccountAppService accounts = new() { IsLoggedIn = OnThreadPool(static _ => true) };
		RecordingNotifications notifications = new();
		FakeTimeProvider time = new();
		using DialogHostViewModel dialogs = new();
		using AccountViewModel viewModel = new(accounts, new SettingsAppService(new FakeConfigManager()), dialogs, notifications, time);

		Task login = viewModel.QrCodeLoginCommand.Execute().ToTask(cancellationToken);
		// 不知道轮询的定时器何时建好，所以一直推进
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
		return new AccountViewModel(accounts, new SettingsAppService(new FakeConfigManager()), new DialogHostViewModel(), new RecordingNotifications(), TimeProvider.System);
	}

	private static Task<bool?> WhenLoggedInAsync(AccountViewModel viewModel, bool isLoggedIn, CancellationToken cancellationToken)
	{
		return viewModel.WhenChanged(static viewModel => viewModel.IsLoggedIn).Keep(value => value == isLoggedIn).FirstAsync(cancellationToken);
	}

	private static Func<string?, CancellationToken, Task<bool>> OnThreadPool(Func<string?, bool> isLoggedIn)
	{
		return (cookie, cancellationToken) => Task.Run(() => isLoggedIn(cookie), cancellationToken);
	}

	private sealed class FakeAccountAppService : IAccountAppService
	{
		private int _saves;
		private int _checks;
		private int _renewals;

		public required Func<string?, CancellationToken, Task<bool>> IsLoggedIn { get; init; }

		public Func<CookieRenewal> Renew { get; set; } = static () => CookieRenewal.Skipped;

		public DateTimeOffset? ExpiresAt { get; init; }

		public bool IsRenewable { get; init; } = true;

		public string? Cookie { get; set; }

		public int Saves => Volatile.Read(ref _saves);

		public int Checks => Volatile.Read(ref _checks);

		public int Renewals => Volatile.Read(ref _renewals);

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

		public async Task<AccountStatus?> GetStatusAsync(CancellationToken cancellationToken = default)
		{
			Interlocked.Increment(ref _checks);
			bool isLoggedIn = await IsLoggedIn(Cookie, cancellationToken);
			return isLoggedIn ? new AccountStatus(new CurrentUser(1, "tester"), ExpiresAt, IsRenewable) : null;
		}

		public Task<CookieRenewal> RenewCookieAsync(CancellationToken cancellationToken = default)
		{
			Interlocked.Increment(ref _renewals);
			return Task.FromResult(Renew());
		}

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
