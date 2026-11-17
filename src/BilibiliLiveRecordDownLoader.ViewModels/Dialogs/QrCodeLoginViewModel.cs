namespace BilibiliLiveRecordDownLoader.ViewModels.Dialogs;

public sealed partial class QrCodeLoginViewModel : DialogViewModel<bool>
{
	private const int MaxConsecutivePollFailures = 5;
	private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

	public QrCodeLoginViewModel()
	{
		RefreshCommand.DisposeWith(Disposables);
	}

	public override string Title => AppLanguage.Current.QrCodeLogin;

	[Reactive]
	public partial string? QrCodeUrl { get; private set; }

	[Reactive]
	public partial QrCodeLoginState State { get; private set; }

	public override void OnOpened()
	{
		Forget(RunAsync);
	}

	[ReactiveCommand]
	private void Refresh()
	{
		Forget(RunAsync);
	}

	private async Task RunAsync(CancellationToken cancellationToken)
	{
		try
		{
			await LoginAsync(cancellationToken);
		}
		catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
		{
			// 出错后显示刷新按钮，用户可以在对话框里重试
			State = QrCodeLoginState.Expired;
			throw;
		}
	}

	private async Task LoginAsync(CancellationToken cancellationToken)
	{
		IAccountAppService accountAppService = AppLocator.Current.GetRequiredService<IAccountAppService>();

		QrCodeUrl = null;
		State = QrCodeLoginState.WaitingForScan;

		QrCodeLoginTicket ticket = await accountAppService.CreateQrCodeLoginAsync(cancellationToken);
		QrCodeUrl = ticket.Url;

		using PeriodicTimer timer = new(PollInterval);
		int failures = 0;

		while (await timer.WaitForNextTickAsync(cancellationToken))
		{
			try
			{
				State = await accountAppService.PollQrCodeLoginAsync(ticket.Key, cancellationToken);
				failures = 0;
			}
			catch (Exception exception) when (!cancellationToken.IsCancellationRequested && exception is HttpRequestException or OperationCanceledException && ++failures < MaxConsecutivePollFailures)
			{
				// 偶发的网络错误不影响扫码，下次继续轮询
				continue;
			}

			switch (State)
			{
				case QrCodeLoginState.Succeeded:
				{
					Close(true);
					return;
				}
				case QrCodeLoginState.Expired:
				{
					return;
				}
			}
		}
	}
}
