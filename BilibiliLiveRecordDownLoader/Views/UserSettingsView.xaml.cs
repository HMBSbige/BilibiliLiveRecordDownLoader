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
			});
		}
	}
}
