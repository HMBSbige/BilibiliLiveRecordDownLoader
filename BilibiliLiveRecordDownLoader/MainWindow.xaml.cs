using BilibiliLiveRecordDownLoader.Interfaces;
using BilibiliLiveRecordDownLoader.ViewModels;
using BilibiliLiveRecordDownLoader.Views;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BilibiliLiveRecordDownLoader
{
	public partial class MainWindow
	{
		private IDisposable? _logServices;

		public MainWindow(ILogger<MainWindow> logger,
			IConfigService configService)
		{
			InitializeComponent();
			ViewModel = new MainWindowViewModel(this, logger, configService);

			this.WhenActivated(d =>
			{
				ViewModel.DisposeWith(d);

				this.Bind(ViewModel,
					vm => vm.ConfigService.Config.RoomId,
					v => v.RoomIdTextBox.Text,
					x => $@"{x}",
					x => long.TryParse(x, out var v) ? v : 732).DisposeWith(d);

				RoomIdTextBox.Events().KeyUp.Subscribe(args =>
				{
					if (args.Key != Key.Enter)
					{
						return;
					}
					ViewModel.TriggerLiveRecordListQuery = !ViewModel.TriggerLiveRecordListQuery;
				}).DisposeWith(d);

				this.OneWayBind(ViewModel, vm => vm.ImageUri, v => v.FaceImage.Source, url => url == null ? null : new BitmapImage(new Uri(url))).DisposeWith(d);
				this.OneWayBind(ViewModel, vm => vm.Name, v => v.NameTextBlock.Text).DisposeWith(d);
				this.OneWayBind(ViewModel, vm => vm.Uid, v => v.UIdTextBlock.Text, i => $@"UID: {i}").DisposeWith(d);
				this.OneWayBind(ViewModel, vm => vm.Level, v => v.LvTextBlock.Text, i => $@"Lv{i}").DisposeWith(d);

				this.Bind(ViewModel, vm => vm.ConfigService.Config.MainDir, v => v.MainDirTextBox.Text).DisposeWith(d);

				this.OneWayBind(ViewModel, vm => vm.DiskUsageProgressBarText, v => v.DiskUsageProgressBarTextBlock.Text).DisposeWith(d);

				this.OneWayBind(ViewModel, vm => vm.DiskUsageProgressBarValue, v => v.DiskUsageProgressBar.Progress).DisposeWith(d);

				this.OneWayBind(ViewModel, vm => vm.DiskUsageProgressBarValue, v => v.DiskUsageProgressBar.Foreground,
								p => p > 90
										? new SolidColorBrush(Colors.Red)
										: new SolidColorBrush(Color.FromRgb(38, 160, 218))).DisposeWith(d);

				this.BindCommand(ViewModel, viewModel => viewModel.SelectMainDirCommand, view => view.SelectMainDirButton).DisposeWith(d);

				this.BindCommand(ViewModel, viewModel => viewModel.OpenMainDirCommand, view => view.OpenMainDirButton).DisposeWith(d);

				this.OneWayBind(ViewModel, vm => vm.LiveRecordList, v => v.LiveRecordListDataGrid.ItemsSource).DisposeWith(d);

				this.OneWayBind(ViewModel, vm => vm.RoomId, v => v.RoomIdTextBlock.Text, i => $@"房间号: {i}").DisposeWith(d);
				this.OneWayBind(ViewModel, vm => vm.ShortRoomId, v => v.ShortRoomIdTextBlock.Text, i => $@"短号: {i}").DisposeWith(d);
				this.OneWayBind(ViewModel, vm => vm.RecordCount, v => v.RecordCountTextBlock.Text, i => $@"列表总数: {i}").DisposeWith(d);
				this.OneWayBind(ViewModel, vm => vm.IsLiveRecordBusy, v => v.LiveRecordBusyIndicator.IsBusy).DisposeWith(d);

				this.BindCommand(ViewModel, vm => vm.CopyLiveRecordDownloadUrlCommand, v => v.CopyLiveRecordDownloadUrlMenuItem).DisposeWith(d);
				this.BindCommand(ViewModel, vm => vm.OpenLiveRecordUrlCommand, v => v.OpenLiveRecordUrlMenuItem).DisposeWith(d);
				this.BindCommand(ViewModel, vm => vm.DownLoadCommand, v => v.DownLoadMenuItem).DisposeWith(d);
				this.BindCommand(ViewModel, vm => vm.OpenDirCommand, v => v.OpenDirMenuItem).DisposeWith(d);

				this.BindCommand(ViewModel, vm => vm.ShowWindowCommand, v => v.NotifyIcon, nameof(NotifyIcon.TrayLeftMouseUp)).DisposeWith(d);

				this.BindCommand(ViewModel, vm => vm.ShowWindowCommand, v => v.ShowMenuItem).DisposeWith(d);
				this.BindCommand(ViewModel, vm => vm.ExitCommand, v => v.ExitMenuItem).DisposeWith(d);

				this.OneWayBind(ViewModel, vm => vm.TaskList, v => v.TaskListDataGrid.ItemsSource).DisposeWith(d);
				this.BindCommand(ViewModel, vm => vm.StopTaskCommand, v => v.StopTaskMenuItem).DisposeWith(d);

				this.Bind(ViewModel,
					vm => vm.ConfigService.Config.DownloadThreads,
					v => v.ThreadsTextBox.Value,
					x => x,
					x => x.HasValue ? Convert.ToByte(x.Value) : (byte)8).DisposeWith(d);

				Observable.FromEventPattern(LogTextBox, nameof(LogTextBox.TextChanged)).Subscribe(_ =>
				{
					if (LogTextBox.LineCount > 2000)
					{
						_logServices?.Dispose();
						LogTextBox.Clear();
						_logServices = CreateLogService();
					}
				}).DisposeWith(d);

				_logServices = CreateLogService();

				LiveRecordListDataGrid.Events().Loaded.Subscribe(_ => LiveRecordListDataGrid.GridColumnSizer = new GridColumnSizerExt(LiveRecordListDataGrid)).DisposeWith(d);

				#region CloseReasonHack

				AddCloseReasonHook();

				this.Events()
						.Closing.Subscribe(e =>
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

		private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
		{
			if (CloseReason is not CloseReason.UserClosing and not CloseReason.None)
			{
				RemoveCloseReasonHook();
				return IntPtr.Zero;
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

			return IntPtr.Zero;
		}

		#endregion

		private IDisposable CreateLogService()
		{
			return ((App)System.Windows.Application.Current).SubjectMemorySink.LogSubject
					.ObserveOnDispatcher()
					.Subscribe(str => LogTextBox.AppendText(str));
		}
	}
}
