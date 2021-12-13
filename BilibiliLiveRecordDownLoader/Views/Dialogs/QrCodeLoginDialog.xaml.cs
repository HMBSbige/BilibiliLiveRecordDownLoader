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

namespace BilibiliLiveRecordDownLoader.Views.Dialogs
{
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

			using var qrGenerator = new QRCodeGenerator();
			using var qrCodeData = qrGenerator.CreateQrCode(data.url, QRCodeGenerator.ECCLevel.H, true);
			using var qrCode = new XamlQRCode(qrCodeData);
			QrCodeImage.Source = qrCode.GetGraphic(20);

			_loginInfoMonitor = CreateMonitor();

			PrimaryButtonCommand = ReactiveCommand.CreateFromTask(GetLoginInfoAsync);
		}

		private IDisposable CreateMonitor()
		{
			return Observable.Interval(TimeSpan.FromSeconds(3))
				.ObserveOnDispatcher()
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
				Cookie = await _apiClient.GetLoginInfoAsync(_data.oauthKey!);
				if (!string.IsNullOrEmpty(Cookie))
				{
					Dispose();
				}
			}
			catch (HttpRequestException ex)
			{
				_logger.LogDebug(ex.Message);
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
		}
	}
}
