using BilibiliLiveRecordDownLoader.Enums;
using BilibiliLiveRecordDownLoader.Utils;
using BilibiliLiveRecordDownLoader.ViewModels;
using ModernWpf;
using ReactiveMarbles.ObservableEvents;
using ReactiveUI;
using System.Reactive.Disposables;

namespace BilibiliLiveRecordDownLoader.Views;

public partial class SettingView
{
	public SettingView(SettingViewModel viewModel)
	{
		InitializeComponent();
		ViewModel = viewModel;

		this.WhenActivated(d =>
		{
			ViewModel.CreateDiskMonitor().DisposeWith(d);
			ViewModel.CheckStartupStatus();

			this.Bind(ViewModel, vm => vm.Config.MainDir, v => v.MainDirTextBox.Text).DisposeWith(d);

			this.OneWayBind(ViewModel, vm => vm.DiskUsageProgressBarText, v => v.DiskUsageProgressBarTextBlock.Text).DisposeWith(d);

			this.OneWayBind(ViewModel, vm => vm.DiskUsageProgressBarValue, v => v.DiskUsageProgressBar.Value).DisposeWith(d);

			this.OneWayBind(ViewModel, vm => vm.DiskUsageProgressBarValue,
				v => v.DiskUsageProgressBar.Foreground,
				p => p > 90 ? Constants.RedBrush : Constants.NormalDiskUsageBrush).DisposeWith(d);

			this.BindCommand(ViewModel, vm => vm.SelectMainDirCommand, v => v.SelectMainDirButton).DisposeWith(d);

			this.BindCommand(ViewModel, vm => vm.OpenMainDirCommand, v => v.OpenMainDirButton).DisposeWith(d);

			this.Bind(ViewModel, vm => vm.Config.IsCheckPreRelease, v => v.IsCheckPreReleaseSwitch.IsOn).DisposeWith(d);
			this.BindCommand(ViewModel, vm => vm.CheckUpdateCommand, v => v.CheckUpdateButton).DisposeWith(d);
			this.OneWayBind(ViewModel, vm => vm.UpdateStatus, v => v.UpdateStatusTextBlock.Text).DisposeWith(d);

			this.Bind(ViewModel, vm => vm.Config.IsAutoConvertMp4, v => v.IsAutoConvertMp4Switch.IsOn).DisposeWith(d);
			this.Bind(ViewModel, vm => vm.Config.IsDeleteAfterConvert, v => v.IsDeleteAfterConvertSwitch.IsOn).DisposeWith(d);

			this.Bind(ViewModel,
				vm => vm.Config.RecorderType,
				v => v.DefaultRecorderTypeRadioButtons.SelectedIndex,
				type => type switch
				{
					RecorderType.Default => 0,
					RecorderType.HttpFlv => 0,
					RecorderType.HlsTs => 1,
					RecorderType.Auto_FFmpeg => 2,
					_ => 0
				},
				i => i switch
				{
					0 => RecorderType.HttpFlv,
					1 => RecorderType.HlsTs,
					2 => RecorderType.Auto_FFmpeg,
					_ => RecorderType.HttpFlv
				}
			).DisposeWith(d);

			this.Bind(ViewModel, vm => vm.IsRunOnStartup, v => v.StartupSwitch.IsOn).DisposeWith(d);
			this.Bind(ViewModel, vm => vm.Config.IsCheckUpdateOnStart, v => v.IsCheckUpdateOnStartSwitch.IsOn).DisposeWith(d);

			StartupSwitch.Events().Toggled.Subscribe(_ => ViewModel.SwitchStartup(StartupSwitch.IsOn)).DisposeWith(d);

			this.Bind(ViewModel,
				vm => vm.Config.Theme,
				v => v.ThemeRadioButtons.SelectedIndex,
				theme => theme switch
				{
					ElementTheme.Default => 0,
					ElementTheme.Light => 1,
					ElementTheme.Dark => 2,
					_ => 0
				},
				i => i switch
				{
					0 => ElementTheme.Default,
					1 => ElementTheme.Light,
					2 => ElementTheme.Dark,
					_ => ElementTheme.Default
				}
			).DisposeWith(d);
		});
	}
}
