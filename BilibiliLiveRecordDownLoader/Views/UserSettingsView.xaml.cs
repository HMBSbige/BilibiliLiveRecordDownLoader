using BilibiliLiveRecordDownLoader.ViewModels;
using ReactiveUI;
using System.Reactive.Disposables;

namespace BilibiliLiveRecordDownLoader.Views
{
	public partial class UserSettingsView
	{
		public UserSettingsView(UserSettingsViewModel viewModel)
		{
			InitializeComponent();
			ViewModel = viewModel;

			this.WhenActivated(d =>
			{
				this.Bind(ViewModel, vm => vm.Config.UserAgent, v => v.UserAgentTextBox.Text).DisposeWith(d);
				this.Bind(ViewModel, vm => vm.Config.Cookie, v => v.CookieTextBox.Text).DisposeWith(d);
				this.Bind(ViewModel, vm => vm.Config.IsUseProxy, v => v.ProxySwitch.IsOn).DisposeWith(d);

				this.BindCommand(ViewModel, vm => vm.QrCodeLoginCommand, v => v.GetQrCodeButton).DisposeWith(d);
				this.BindCommand(ViewModel, vm => vm.CheckLoginCommand, v => v.CheckLoginButton).DisposeWith(d);
				this.OneWayBind(ViewModel, vm => vm.LoginStatus, v => v.LoginStatusTextBlock.Text).DisposeWith(d);
				this.OneWayBind(ViewModel, vm => vm.LoginStatusForeground, v => v.LoginStatusTextBlock.Foreground).DisposeWith(d);

				ViewModel.CheckLoginCommand.Execute();
			});
		}
	}
}
