using BilibiliApi.Clients;
using BilibiliApi.Model.Login.QrCode.GetLoginUrl;
using BilibiliLiveRecordDownLoader.Services;
using Microsoft.Extensions.Logging;
using QRCoder;
using QRCoder.Xaml;
using ReactiveUI;
using System.Net.Http;
using System.Reactive;
using System.Reactive.Linq;
using System.Windows;

namespace BilibiliLiveRecordDownLoader.Views.Dialogs;

public partial class QrCodeLoginDialog
{
	private readonly IDisposable _loginInfoMonitor;
	private readonly ILogger _logger;
	private readonly BilibiliApiClient _apiClient;
	private readonly GetLoginUrlData _data;

	public string? Cookie { get; private set; }

	public QrCodeLoginDialog(GetLoginUrlData data)
	{
		_logger = DI.GetLogger<QrCodeLoginDialog>();
		_apiClient = DI.GetRequiredService<BilibiliApiClient>();
		_data = data;

		InitializeComponent();

		using QRCodeGenerator qrGenerator = new();
		using QRCodeData qrCodeData = qrGenerator.CreateQrCode(data.url ?? string.Empty, QRCodeGenerator.ECCLevel.H, true);
		using XamlQRCode qrCode = new(qrCodeData);
		QrCodeImage.Source = qrCode.GetGraphic(20);

		_loginInfoMonitor = CreateMonitor();

		PrimaryButtonCommand = ReactiveCommand.CreateFromTask(GetLoginInfoAsync);
	}

	private IDisposable CreateMonitor()
	{
		return Observable.Interval(TimeSpan.FromSeconds(3))
			.TakeWhile(x => x < TimeSpan.FromSeconds(180).TotalSeconds)
			.ObserveOn(RxApp.MainThreadScheduler)
			.SelectMany(Async)
			.Subscribe();

		async Task<Unit> Async(long i)
		{
			await GetLoginInfoAsync();
			return default;
		}
	}

	private async Task GetLoginInfoAsync()
	{
		try
		{
			Cookie = await _apiClient.GetLoginInfoAsync(_data.qrcode_key!);
			if (!string.IsNullOrEmpty(Cookie))
			{
				Dispose();
			}
		}
		catch (HttpRequestException ex)
		{
			_logger.LogDebug(@"{message}", ex.Message);
			if (!ex.Message.Contains(@"未扫码"))
			{
				MessageTextBlock.Text = ex.Message;
				QrCodeImage.Visibility = Visibility.Hidden;
			}
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, @"获取登录信息失败");
		}
	}

	public override void Dispose()
	{
		_loginInfoMonitor.Dispose();
		base.Dispose();

		GC.SuppressFinalize(this);
	}
}
