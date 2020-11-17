using BilibiliLiveRecordDownLoader.Utils;
using BilibiliLiveRecordDownLoader.ViewModels;
using ReactiveUI;
using Splat;
using System;
using System.Reactive.Disposables;

namespace BilibiliLiveRecordDownLoader.Views
{
	public partial class SettingView
	{
		public SettingView()
		{
			InitializeComponent();
			ViewModel = Locator.Current.GetService<SettingViewModel>();

			this.WhenActivated(d =>
			{
				this.Bind(ViewModel, vm => vm.Config.MainDir, v => v.MainDirTextBox.Text).DisposeWith(d);

				this.OneWayBind(ViewModel, vm => vm.Global.DiskUsageProgressBarText, v => v.DiskUsageProgressBarTextBlock.Text).DisposeWith(d);

				this.OneWayBind(ViewModel, vm => vm.Global.DiskUsageProgressBarValue, v => v.DiskUsageProgressBar.Value).DisposeWith(d);

				this.OneWayBind(ViewModel, vm => vm.Global.DiskUsageProgressBarValue,
						v => v.DiskUsageProgressBar.Foreground,
						p => p > 90 ? Constants.RedBrush : Constants.NormalDiskUsageBrush).DisposeWith(d);

				this.BindCommand(ViewModel, viewModel => viewModel.SelectMainDirCommand, view => view.SelectMainDirButton).DisposeWith(d);

				this.BindCommand(ViewModel, viewModel => viewModel.OpenMainDirCommand, view => view.OpenMainDirButton).DisposeWith(d);

				this.Bind(ViewModel,
						vm => vm.Config.DownloadThreads,
						v => v.ThreadsTextBox.Value,
						x => x,
						Convert.ToByte).DisposeWith(d);

				this.Bind(ViewModel, vm => vm.Config.IsCheckUpdateOnStart, v => v.IsCheckUpdateOnStartSwitch.IsOn).DisposeWith(d);
				this.Bind(ViewModel, vm => vm.Config.IsCheckPreRelease, v => v.IsCheckPreReleaseSwitch.IsOn).DisposeWith(d);
				this.BindCommand(ViewModel, vm => vm.CheckUpdateCommand, v => v.CheckUpdateButton).DisposeWith(d);
				this.OneWayBind(ViewModel, vm => vm.Global.UpdateStatus, v => v.UpdateStatusTextBlock.Text).DisposeWith(d);
			});
		}
	}
}
