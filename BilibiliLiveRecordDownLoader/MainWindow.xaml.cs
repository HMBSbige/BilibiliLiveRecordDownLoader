using BilibiliApi.Enums;
using BilibiliLiveRecordDownLoader.Services;
using BilibiliLiveRecordDownLoader.ViewModels;
using Hardcodet.Wpf.TaskbarNotification;
using ModernWpf.Controls;
using ReactiveUI;
using System;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Threading;

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
			MessageInteractions message)
		{
			InitializeComponent();
			ViewModel = viewModel;

			this.WhenActivated(d =>
			{
				this.BindCommand(ViewModel, vm => vm.ShowWindowCommand, v => v.NotifyIcon, nameof(NotifyIcon.TrayLeftMouseUp)).DisposeWith(d);
				this.BindCommand(ViewModel, vm => vm.ShowWindowCommand, v => v.ShowMenuItem).DisposeWith(d);
				this.BindCommand(ViewModel, vm => vm.ExitCommand, v => v.ExitMenuItem).DisposeWith(d);

				this.Bind(ViewModel, vm => vm.HostScreen.Router, v => v.RoutedViewHost.Router).DisposeWith(d);

				Observable.FromEventPattern<NavigationViewSelectionChangedEventArgs>(NavigationView, nameof(NavigationView.SelectionChanged))
				.Subscribe(args =>
				{
					if (args.EventArgs.IsSettingsSelected)
					{
						ViewModel.HostScreen.Router.Navigate.Execute(settings);
						return;
					}

					if (args.EventArgs.SelectedItem is not NavigationViewItem { Tag: string tag })
					{
						return;
					}

					switch (tag)
					{
						case @"1":
						{
							ViewModel.HostScreen.Router.Navigate.Execute(liveRecordList);
							break;
						}
						case @"2":
						{
							ViewModel.HostScreen.Router.Navigate.Execute(taskList);
							break;
						}
						case @"3":
						{
							ViewModel.HostScreen.Router.Navigate.Execute(log);
							break;
						}
						case @"4":
						{
							ViewModel.HostScreen.Router.Navigate.Execute(streamRecord);
							break;
						}
					}
				}).DisposeWith(d);

				NavigationView.SelectedItem = NavigationView.MenuItems.OfType<NavigationViewItem>().First();

				this.Bind(ViewModel, vm => vm.Config.MainWindowsWidth, v => v.Width).DisposeWith(d);
				this.Bind(ViewModel, vm => vm.Config.MainWindowsHeight, v => v.Height).DisposeWith(d);

				message.ShowLiveStatus.RegisterHandler(async context =>
				{
					var room = context.Input;
					if (room.LiveStatus == LiveStatus.直播)
					{
						NotifyIcon.ShowBalloonTip($@"{room.UserName} 开播了！", room.Title, BalloonIcon.Info);
					}
					context.SetOutput(default);
				}).DisposeWith(d);

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
