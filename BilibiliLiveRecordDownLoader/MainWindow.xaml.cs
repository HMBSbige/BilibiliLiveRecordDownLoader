using System;
using System.Reactive.Disposables;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BilibiliLiveRecordDownLoader.ViewModels;
using ReactiveUI;

namespace BilibiliLiveRecordDownLoader
{
    public partial class MainWindow
    {
        public MainWindow()
        {
            InitializeComponent();
            ViewModel = new MainWindowViewModel();

            this.WhenActivated(d =>
            {
                this.Bind(ViewModel, vm => vm.Config.RoomId, v => v.RoomIdTextBox.Text).DisposeWith(d);

                this.OneWayBind(ViewModel, vm => vm.ImageUri, v => v.FaceImage.Source,
                                url => url == null ? null : new BitmapImage(new Uri(url))).DisposeWith(d);

                this.OneWayBind(ViewModel, vm => vm.Name, v => v.NameTextBlock.Text).DisposeWith(d);

                this.OneWayBind(ViewModel, vm => vm.Uid, v => v.UIdTextBlock.Text).DisposeWith(d);

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

                ViewModel.DisposeWith(d);
            });
        }
    }
}
