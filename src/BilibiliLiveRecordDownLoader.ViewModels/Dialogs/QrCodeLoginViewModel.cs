namespace BilibiliLiveRecordDownLoader.ViewModels.Dialogs;

public sealed partial class QrCodeLoginViewModel : DialogViewModel<bool>
{
	private const int MaxConsecutivePollFailures = 5;
	private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

	private readonly IAccountAppService _accountAppService;

	public QrCodeLoginViewModel(IAccountAppService accountAppService)
	{
		_accountAppService = accountAppService;
		RefreshCommand.DisposeWith(Disposables);
	}

	public override string Title => AppLanguage.Current.QrCodeLogin;

	[Reactive]
	public partial string? QrCodeUrl { get; private set; }

	[Reactive]
	public partial bool IsLoadingQrCode { get; private set; }

	[Reactive]
	public partial QrCodeLoginState State { get; private set; }

	[Reactive]
	public partial string? ErrorMessage { get; private set; }

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
			// 借用过期状态的刷新按钮重试
			ErrorMessage = exception.Message;
			State = QrCodeLoginState.Expired;
		}
	}

	private async Task LoginAsync(CancellationToken cancellationToken)
	{
		QrCodeUrl = null;
		ErrorMessage = null;
		State = QrCodeLoginState.WaitingForScan;
		IsLoadingQrCode = true;
		QrCodeLoginTicket ticket;

		try
		{
			ticket = await _accountAppService.CreateQrCodeLoginAsync(cancellationToken);
		}
		finally
		{
			IsLoadingQrCode = false;
		}

		QrCodeUrl = ticket.Url;

		using PeriodicTimer timer = new(PollInterval);
		int failures = 0;

		while (await timer.WaitForNextTickAsync(cancellationToken))
		{
			try
			{
				State = await _accountAppService.PollQrCodeLoginAsync(ticket.Key, cancellationToken);
				failures = 0;
			}
			catch (Exception exception) when (exception is HttpRequestException or TimeoutException && ++failures < MaxConsecutivePollFailures)
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
