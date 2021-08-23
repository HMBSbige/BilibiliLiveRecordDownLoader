using BilibiliApi.Enums;
using BilibiliLiveRecordDownLoader.Models;
using BilibiliLiveRecordDownLoader.ViewModels;
using Hardcodet.Wpf.TaskbarNotification;
using ModernWpf.Controls;
using ReactiveMarbles.ObservableEvents;
using ReactiveUI;
using System;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;

namespace BilibiliLiveRecordDownLoader
{
	public partial class MainWindow
	{
		public MainWindow(
			MainWindowViewModel viewModel,
			LiveRecordListViewModel liveRecordList,
			TaskListViewModel taskList,
			LogViewModel log,
			SettingViewModel settings,
			StreamRecordViewModel streamRecord,
			UserSettingsViewModel userSettings,
			FFmpegCommandViewModel ffmpegCommand)
		{
			InitializeComponent();
			ViewModel = viewModel;

			this.WhenActivated(d =>
			{
				this.BindCommand(ViewModel, vm => vm.ShowWindowCommand, v => v.NotifyIcon, nameof(NotifyIcon.TrayLeftMouseUp)).DisposeWith(d);
				this.BindCommand(ViewModel, vm => vm.ShowWindowCommand, v => v.ShowMenuItem).DisposeWith(d);
				this.BindCommand(ViewModel, vm => vm.ExitCommand, v => v.ExitMenuItem).DisposeWith(d);

				this.Bind(ViewModel, vm => vm.Router, v => v.RoutedViewHost.Router).DisposeWith(d);

				NavigationView.Events().SelectionChanged
				.Subscribe(parameter =>
				{
					if (parameter.args.IsSettingsSelected)
					{
						ViewModel.Router.NavigateAndReset.Execute(settings);
						return;
					}

					if (parameter.args.SelectedItem is not NavigationViewItem { Tag: string tag })
					{
						return;
					}

					switch (tag)
					{
						case @"1":
						{
							ViewModel.Router.NavigateAndReset.Execute(liveRecordList);
							break;
						}
						case @"2":
						{
							ViewModel.Router.NavigateAndReset.Execute(taskList);
							break;
						}
						case @"3":
						{
							ViewModel.Router.NavigateAndReset.Execute(log);
							break;
						}
						case @"4":
						{
							ViewModel.Router.NavigateAndReset.Execute(streamRecord);
							break;
						}
						case @"5":
						{
							ViewModel.Router.NavigateAndReset.Execute(userSettings);
							break;
						}
						case @"6":
						{
							ViewModel.Router.NavigateAndReset.Execute(ffmpegCommand);
							break;
						}
					}
				}).DisposeWith(d);

				NavigationView.SelectedItem = NavigationView.MenuItems.OfType<NavigationViewItem>().First();

				this.Bind(ViewModel, vm => vm.Config.MainWindowsWidth, v => v.Width).DisposeWith(d);
				this.Bind(ViewModel, vm => vm.Config.MainWindowsHeight, v => v.Height).DisposeWith(d);

				MessageBus.Current.Listen<RoomStatus>()
						.Where(room => room.LiveStatus == LiveStatus.直播)
						.ObserveOnDispatcher()
						.Subscribe(room => NotifyIcon.ShowBalloonTip($@"{room.UserName} 开播了！", room.Title, BalloonIcon.Info)).DisposeWith(d);

				#region CloseReasonHack

				AddCloseReasonHook();

				this.Events().Closing.Subscribe(e =>
				{
					if (CloseReason == CloseReason.UserClosing)
					{
						Hide();
						e.Cancel = true;
					}
				}).DisposeWith(d);

				#endregion
			});
		}

		#region CloseReasonHack

		private void AddCloseReasonHook()
		{
			if (PresentationSource.FromDependencyObject(this) is HwndSource source)
			{
				source.AddHook(WindowProc);
			}
		}

		private void RemoveCloseReasonHook()
		{
			if (PresentationSource.FromDependencyObject(this) is HwndSource source)
			{
				source.RemoveHook(WindowProc);
			}
		}

		public CloseReason CloseReason = CloseReason.None;

		private nint WindowProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
		{
			if (CloseReason is not CloseReason.UserClosing and not CloseReason.None)
			{
				RemoveCloseReasonHook();
				return 0;
			}

			switch (msg)
			{
				case 0x10:
				{
					CloseReason = CloseReason.UserClosing;
					break;
				}
				case 0x11:
				case 0x16:
				{
					CloseReason = CloseReason.WindowsShutDown;
					break;
				}
				case 0x112:
				{
					if (((ushort)wParam & 0xfff0) == 0xf060)
					{
						CloseReason = CloseReason.UserClosing;
					}

					break;
				}
			}

			return 0;
		}

		#endregion
	}
}
