using BilibiliLive.Models;
using BilibiliLiveRecordDownLoader.Application.Contracts.Accounts;
using BilibiliLiveRecordDownLoader.ViewModels;
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
		using AccountViewModel viewModel = new(accounts);
		ConcurrentQueue<Exception> thrown = new();
		using IDisposable subscription = viewModel.CheckLoginCommand.ThrownExceptions.Subscribe(thrown.Enqueue);

		((ICommand)viewModel.CheckLoginCommand).Execute(null);
		// 异常是异步交给订阅者的，等它到了再结束，否则会落到全局处理
		await TestWait.UntilAsync(() => !thrown.IsEmpty, "the exception reaches the subscriber", cancellationToken);
		Exception reported = await Assert.That(thrown).HasSingleItem();

		using (Assert.Multiple())
		{
			await Assert.That(viewModel.IsLoggedIn).IsFalse();
			await Assert.That(reported).IsSameReferenceAs(failure);
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
		using AccountViewModel viewModel = new(accounts);
		await viewModel.LoadAsync(cancellationToken);

		viewModel.Cookie = "new";
		await TestWait.UntilAsync(() => viewModel.IsLoggedIn is true, "the new cookie is checked as logged in", cancellationToken);

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
				TaskCompletionSource<bool> check = new();
				checks.Enqueue(check);
				return check.Task;
			}
		};
		using AccountViewModel viewModel = new(accounts);
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
		int checks = 0;
		FakeAccountAppService accounts = new()
		{
			Cookie = "old",
			IsLoggedIn = OnThreadPool(_ =>
			{
				Interlocked.Increment(ref checks);
				return true;
			})
		};
		using AccountViewModel viewModel = new(accounts);
		await viewModel.LoadAsync(cancellationToken);
		((ICommand)viewModel.CheckLoginCommand).Execute(null);
		await TestWait.UntilAsync(() => viewModel.IsLoggedIn is true, "the login check reports logged in", cancellationToken);
		int saves = accounts.Saves;

		((ICommand)viewModel.LogoutCommand).Execute(null);
		await TestWait.UntilAsync(() => viewModel.IsLoggedIn is false, "the logout is shown", cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(viewModel.Cookie).IsNull();
			await Assert.That(Volatile.Read(ref checks)).IsEqualTo(1);
			await Assert.That(accounts.Saves).IsEqualTo(saves);
		}
	}

	// 和真实的请求一样，结果在别的线程上返回；参数是检查时服务里的 Cookie
	private static Func<string?, CancellationToken, Task<bool>> OnThreadPool(Func<string?, bool> isLoggedIn)
	{
		return (cookie, cancellationToken) => Task.Run(() => isLoggedIn(cookie), cancellationToken);
	}

	private sealed class FakeAccountAppService : IAccountAppService
	{
		private int _saves;

		public required Func<string?, CancellationToken, Task<bool>> IsLoggedIn { get; init; }

		public string? Cookie { get; set; }

		public int Saves => Volatile.Read(ref _saves);

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
			return IsLoggedIn(Cookie, cancellationToken);
		}

		public Task<QrCodeLoginTicket> CreateQrCodeLoginAsync(CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task<QrCodeLoginState> PollQrCodeLoginAsync(string key, CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task LogoutAsync(CancellationToken cancellationToken = default)
		{
			Cookie = null;
			return Task.CompletedTask;
		}
	}
}
