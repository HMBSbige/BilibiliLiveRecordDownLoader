using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BilibiliLiveRecordDownLoader.Enums;
using BilibiliLiveRecordDownLoader.ViewModels;
using BilibiliLiveRecordDownLoader.Views;
using ReactiveUI;
using Syncfusion.UI.Xaml.Grid;

namespace BilibiliLiveRecordDownLoader
{
    public partial class MainWindow
    {
        public MainWindow()
        {
            InitializeComponent();
            ViewModel = new MainWindowViewModel(this);

            LiveRecordListDataGrid.GridColumnSizer = new GridColumnSizerExt(LiveRecordListDataGrid);

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
                    if (args.Key != Key.Enter) return;
                    ViewModel.TriggerLiveRecordListQuery = !ViewModel.TriggerLiveRecordListQuery;
                }).DisposeWith(d);

                this.OneWayBind(ViewModel, vm => vm.ImageUri, v => v.FaceImage.Source,
                                url => url == null ? null : new BitmapImage(new Uri(url))).DisposeWith(d);

                this.OneWayBind(ViewModel, vm => vm.Name, v => v.NameTextBlock.Text).DisposeWith(d);

                this.OneWayBind(ViewModel, vm => vm.Uid, v => v.UIdTextBlock.Text, i => $@"UID: {i}").DisposeWith(d);

                this.OneWayBind(ViewModel, vm => vm.Level, v => v.LvTextBlock.Text, i => $@"Lv{i}").DisposeWith(d);

                this.Bind(ViewModel, vm => vm.Config.MainDir, v => v.MainDirTextBox.Text).DisposeWith(d);

                this.OneWayBind(ViewModel, vm => vm.DiskUsageProgressBarText, v => v.DiskUsageProgressBarTextBlock.Text).DisposeWith(d);

                this.OneWayBind(ViewModel, vm => vm.DiskUsageProgressBarValue, v => v.DiskUsageProgressBar.Value).DisposeWith(d);

                this.OneWayBind(ViewModel, vm => vm.DiskUsageProgressBarValue, v => v.DiskUsageProgressBar.Foreground,
                                p => p > 90
                                        ? new SolidColorBrush(Colors.Red)
                                        : new SolidColorBrush(Color.FromRgb(38, 160, 218))).DisposeWith(d);

                this.BindCommand(ViewModel, viewModel => viewModel.SelectMainDirCommand, view => view.SelectMainDirButton).DisposeWith(d);

                this.BindCommand(ViewModel, viewModel => viewModel.OpenMainDirCommand, view => view.OpenMainDirButton).DisposeWith(d);

                this.OneWayBind(ViewModel, vm => vm.LiveRecordList, v => v.LiveRecordListDataGrid.ItemsSource).DisposeWith(d);

                ViewModel.WhenAnyValue(x => x.RoomId)
                         .ObserveOn(RxApp.MainThreadScheduler)
                         .Subscribe(i => RoomIdTextBlock.Text = $@"房间号: {i}")
                         .DisposeWith(d);

                ViewModel.WhenAnyValue(x => x.ShortRoomId)
                        .ObserveOn(RxApp.MainThreadScheduler)
                        .Subscribe(i => ShortRoomIdTextBlock.Text = $@"短号: {i}")
                        .DisposeWith(d);

                ViewModel.WhenAnyValue(x => x.RecordCount)
                        .ObserveOn(RxApp.MainThreadScheduler)
                        .Subscribe(i => RecordCountTextBlock.Text = $@"列表总数: {i}")
                        .DisposeWith(d);

                ViewModel.WhenAnyValue(x => x.IsLiveRecordBusy)
                        .ObserveOn(RxApp.MainThreadScheduler)
                        .Subscribe(b => LiveRecordBusyIndicator.IsBusy = b)
                        .DisposeWith(d);

                ViewModel.WhenAnyValue(x => x.DownloadTaskPool.HasTaskRunning)
                        .ObserveOn(RxApp.MainThreadScheduler)
                        .Subscribe(b => DownloadLiveRecordBusyIndicator.IsBusy = b)
                        .DisposeWith(d);

                this.BindCommand(ViewModel, vm => vm.CopyLiveRecordDownloadUrlCommand, v => v.CopyLiveRecordDownloadUrlMenuItem).DisposeWith(d);
                this.BindCommand(ViewModel, vm => vm.OpenLiveRecordUrlCommand, v => v.OpenLiveRecordUrlMenuItem).DisposeWith(d);
                this.BindCommand(ViewModel, vm => vm.DownLoadCommand, v => v.DownLoadMenuItem).DisposeWith(d);
                this.BindCommand(ViewModel, vm => vm.OpenDirCommand, v => v.OpenDirMenuItem).DisposeWith(d);

                Observable.FromEventPattern(LiveRecordListDataGrid, nameof(LiveRecordListDataGrid.GridContextMenuOpening))
                .Subscribe(args =>
                {
                    if (args.EventArgs is GridContextMenuEventArgs a
                    && a.ContextMenuInfo is GridRecordContextMenuInfo info
                    && info.Record is LiveRecordListViewModel record
                    && record.IsDownloading)
                    {
                        DownLoadMenuItem.Header = @"停止下载";
                    }
                    else
                    {
                        DownLoadMenuItem.Header = @"下载";
                    }
                }).DisposeWith(d);

                this.BindCommand(ViewModel, vm => vm.ShowWindowCommand, v => v.NotifyIcon, nameof(NotifyIcon.TrayMouseDoubleClick)).DisposeWith(d);

                this.BindCommand(ViewModel, vm => vm.ShowWindowCommand, v => v.ShowMenuItem).DisposeWith(d);
                this.BindCommand(ViewModel, vm => vm.ExitCommand, v => v.ExitMenuItem).DisposeWith(d);

                #region CloseReasonHack

                if (PresentationSource.FromDependencyObject(this) is HwndSource source)
                {
                    source.AddHook(WindowProc);
                }

                this.Events().Closing.Subscribe(e =>
                {
                    switch (CloseReason)
                    {
                        case CloseReason.Unknown:
                        case CloseReason.Logoff:
                            break;
                        case CloseReason.User:
                        {
                            Visibility = Visibility.Hidden;
                            e.Cancel = true;
                            break;
                        }
                    }
                }).DisposeWith(d);

                #endregion

                this.Bind(ViewModel,
                    vm => vm.Config.DownloadThreads,
                    v => v.ThreadsTextBox.Text,
                    x => $@"{x}",
                    x => byte.TryParse(x, out var v) ? v : (byte)8).DisposeWith(d);
            });
        }

        #region CloseReasonHack

        public CloseReason CloseReason = CloseReason.Unknown;

        private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            switch (msg)
            {
                case 0x11:
                case 0x16:
                    CloseReason = CloseReason.Logoff;
                    break;
                case 0x112:
                    if (((ushort)wParam & 0xfff0) == 0xf060)
                    {
                        CloseReason = CloseReason.User;
                    }
                    break;
            }
            return IntPtr.Zero;
        }

        #endregion
    }
}
