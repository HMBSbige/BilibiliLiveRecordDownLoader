using BilibiliLive.Models;
using BilibiliLiveRecordDownLoader.Application.Contracts.Accounts;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
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
	[Timeout(30_000)]
	[MethodDataSource(nameof(CheckFailures))]
	public async Task FailedCheckLeavesTheCheckingStateAsync(Exception failure, CancellationToken cancellationToken)
	{
		FakeAccountAppService accounts = new() { IsLoggedIn = _ => throw failure };
		using AccountViewModel viewModel = new(accounts, new FakeConfigManager());
		ConcurrentQueue<Exception> thrown = new();
		using IDisposable subscription = viewModel.CheckLoginCommand.ThrownExceptions.Subscribe(thrown.Enqueue);

		((ICommand)viewModel.CheckLoginCommand).Execute(null);
		// 异常是异步交给订阅者的，等它到了再结束，否则会落到全局处理
		await TestWait.UntilAsync(() => !thrown.IsEmpty, "the exception reaches the subscriber", cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(viewModel.IsLoggedIn).IsFalse();
			await Assert.That(thrown.Single()).IsSameReferenceAs(failure);
		}
	}

	[Test]
	[Timeout(30_000)]
	public async Task EditedCookieIsCheckedAfterItIsSavedAsync(CancellationToken cancellationToken)
	{
		AppConfig config = AppConfig.CreateDefault();
		config.Cookie = "old";
		FakeConfigManager configManager = new(config);
		ConcurrentQueue<string> checkedCookies = new();
		FakeAccountAppService accounts = new() { IsLoggedIn = _ => { checkedCookies.Enqueue(config.Cookie ?? string.Empty); return true; } };
		using AccountViewModel viewModel = new(accounts, configManager);
		viewModel.ApplyConfig(config);

		viewModel.Cookie = "new";
		await TestWait.UntilAsync(() => viewModel.IsLoggedIn is true, "the new cookie is checked as logged in", cancellationToken);

		await Assert.That(checkedCookies).IsEquivalentTo(["new"], StringComparer.Ordinal);
	}

	[Test]
	[Timeout(30_000)]
	public async Task CookieReloadedAfterLogoutIsNotSavedOrCheckedAgainAsync(CancellationToken cancellationToken)
	{
		AppConfig config = AppConfig.CreateDefault();
		config.Cookie = "old";
		FakeConfigManager configManager = new(config);
		int checks = 0;
		FakeAccountAppService accounts = new()
		{
			IsLoggedIn = _ =>
			{
				Interlocked.Increment(ref checks);
				return true;
			},
			Logout = () => config.Cookie = null
		};
		using AccountViewModel viewModel = new(accounts, configManager);
		viewModel.ApplyConfig(config);
		((ICommand)viewModel.CheckLoginCommand).Execute(null);
		await TestWait.UntilAsync(() => viewModel.IsLoggedIn is true, "the login check reports logged in", cancellationToken);
		int updates = configManager.UpdateCount;

		((ICommand)viewModel.LogoutCommand).Execute(null);
		await TestWait.UntilAsync(() => viewModel.IsLoggedIn is false, "the logout is shown", cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(viewModel.Cookie).IsNull();
			await Assert.That(Volatile.Read(ref checks)).IsEqualTo(1);
			await Assert.That(configManager.UpdateCount).IsEqualTo(updates);
		}
	}

	private sealed class FakeAccountAppService : IAccountAppService
	{
		public Func<CancellationToken, bool> IsLoggedIn { get; init; } = static _ => false;

		public Action Logout { get; init; } = static () => { };

		public Task<bool> IsLoggedInAsync(CancellationToken cancellationToken = default)
		{
			return Task.Run(() => IsLoggedIn(cancellationToken), cancellationToken);
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
			Logout();
			return Task.CompletedTask;
		}
	}
}
