using BilibiliLiveRecordDownLoader.ViewModels;
using ReactiveUI;
using Splat;
using System;
using System.Reactive.Disposables;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace BilibiliLiveRecordDownLoader
{
	public partial class MainWindow
	{
		public MainWindow()
		{
			InitializeComponent();
			ViewModel = Locator.Current.GetService<MainWindowViewModel>();

			this.WhenActivated(d =>
			{
				ViewModel.DisposeWith(d);

				this.Bind(ViewModel,
					vm => vm.Config.RoomId,
					v => v.RoomIdTextBox.Text,
					x => $@"{x}",
					x => long.TryParse(x, out var v) ? v : 732).DisposeWith(d);

				RoomIdTextBox.Events().KeyUp.Subscribe(args =>
				{
					if (args.Key != Key.Enter)
					{
						return;
					}
					ViewModel.Global.TriggerLiveRecordListQuery = !ViewModel.Global.TriggerLiveRecordListQuery;
				}).DisposeWith(d);

				this.OneWayBind(ViewModel, vm => vm.Global.ImageUri, v => v.FaceImage.Source, url => url == null ? null : new BitmapImage(new Uri(url))).DisposeWith(d);
				this.OneWayBind(ViewModel, vm => vm.Global.Name, v => v.NameTextBlock.Text).DisposeWith(d);
				this.OneWayBind(ViewModel, vm => vm.Global.Uid, v => v.UIdTextBlock.Text, i => $@"UID: {i}").DisposeWith(d);
				this.OneWayBind(ViewModel, vm => vm.Global.Level, v => v.LvTextBlock.Text, i => $@"Lv{i}").DisposeWith(d);

				this.OneWayBind(ViewModel, vm => vm.Global.RoomId, v => v.RoomIdTextBlock.Text, i => $@"房间号: {i}").DisposeWith(d);
				this.OneWayBind(ViewModel, vm => vm.Global.ShortRoomId, v => v.ShortRoomIdTextBlock.Text, i => $@"短号: {i}").DisposeWith(d);
				this.OneWayBind(ViewModel, vm => vm.Global.RecordCount, v => v.RecordCountTextBlock.Text, i => $@"列表总数: {i}").DisposeWith(d);
				this.OneWayBind(ViewModel, vm => vm.Global.IsLiveRecordBusy, v => v.LiveRecordBusyIndicator.IsActive).DisposeWith(d);

				this.OneWayBind(ViewModel, vm => vm.LiveRecordList, v => v.LiveRecordListDataGrid.ItemsSource).DisposeWith(d);
				this.BindCommand(ViewModel, vm => vm.CopyLiveRecordDownloadUrlCommand, v => v.CopyLiveRecordDownloadUrlMenuItem).DisposeWith(d);
				this.BindCommand(ViewModel, vm => vm.OpenLiveRecordUrlCommand, v => v.OpenLiveRecordUrlMenuItem).DisposeWith(d);
				this.BindCommand(ViewModel, vm => vm.DownLoadCommand, v => v.DownLoadMenuItem).DisposeWith(d);
				this.BindCommand(ViewModel, vm => vm.OpenDirCommand, v => v.OpenDirMenuItem).DisposeWith(d);

				this.BindCommand(ViewModel, vm => vm.ShowWindowCommand, v => v.NotifyIcon, nameof(NotifyIcon.TrayLeftMouseUp)).DisposeWith(d);

				this.BindCommand(ViewModel, vm => vm.ShowWindowCommand, v => v.ShowMenuItem).DisposeWith(d);
				this.BindCommand(ViewModel, vm => vm.ExitCommand, v => v.ExitMenuItem).DisposeWith(d);

				this.OneWayBind(ViewModel, vm => vm.TaskList, v => v.TaskListDataGrid.ItemsSource).DisposeWith(d);
				this.BindCommand(ViewModel, vm => vm.StopTaskCommand, v => v.StopTaskMenuItem).DisposeWith(d);
				this.BindCommand(ViewModel, vm => vm.ClearAllTasksCommand, v => v.RemoveTaskMenuItem).DisposeWith(d);

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
