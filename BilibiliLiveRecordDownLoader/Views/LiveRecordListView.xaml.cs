using BilibiliLiveRecordDownLoader.ViewModels;
using ReactiveUI;
using Splat;
using System;
using System.Reactive.Disposables;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace BilibiliLiveRecordDownLoader.Views
{
	public partial class LiveRecordListView
	{
		public LiveRecordListView()
		{
			InitializeComponent();
			ViewModel = Locator.Current.GetService<LiveRecordListViewModel>();

			this.WhenActivated(d =>
			{
				this.Bind(ViewModel,
						vm => vm.Config.RoomId,
						v => v.RoomIdTextBox.Text,
						x => $@"{x}",
						x => long.TryParse(x, out var v) ? v : 732).DisposeWith(d);

				RoomIdTextBox.Events().KeyUp.Subscribe(args =>
				{
					if (args.Key == Key.Enter)
					{
						ViewModel.TriggerLiveRecordListQuery = !ViewModel.TriggerLiveRecordListQuery;
					}
				}).DisposeWith(d);

				this.OneWayBind(ViewModel, vm => vm.ImageUri, v => v.FaceImage.Source, url => url == null ? null : new BitmapImage(new Uri(url))).DisposeWith(d);
				this.OneWayBind(ViewModel, vm => vm.Name, v => v.NameTextBlock.Text).DisposeWith(d);
				this.OneWayBind(ViewModel, vm => vm.Uid, v => v.UIdTextBlock.Text, i => $@"UID: {i}").DisposeWith(d);
				this.OneWayBind(ViewModel, vm => vm.Level, v => v.LvTextBlock.Text, i => $@"Lv{i}").DisposeWith(d);

				this.OneWayBind(ViewModel, vm => vm.RoomId, v => v.RoomIdTextBlock.Text, i => $@"房间号: {i}").DisposeWith(d);
				this.OneWayBind(ViewModel, vm => vm.ShortRoomId, v => v.ShortRoomIdTextBlock.Text, i => $@"短号: {i}").DisposeWith(d);
				this.OneWayBind(ViewModel, vm => vm.RecordCount, v => v.RecordCountTextBlock.Text, i => $@"列表总数: {i}").DisposeWith(d);
				this.OneWayBind(ViewModel, vm => vm.IsLiveRecordBusy, v => v.LiveRecordBusyIndicator.IsActive).DisposeWith(d);

				this.OneWayBind(ViewModel, vm => vm.LiveRecordList, v => v.LiveRecordListDataGrid.ItemsSource).DisposeWith(d);
				this.WhenAnyValue(v => v.LiveRecordListDataGrid.SelectedItem)
						.BindTo(ViewModel, vm => vm.SelectedItem)
						.DisposeWith(d);
				this.WhenAnyValue(v => v.LiveRecordListDataGrid.SelectedItems)
						.BindTo(ViewModel, vm => vm.SelectedItems)
						.DisposeWith(d);
				this.BindCommand(ViewModel,
					vm => vm.DownLoadCommand,
					v => v.DownLoadMenuItem,
					vm => vm.SelectedItems).DisposeWith(d);
				this.BindCommand(ViewModel,
					vm => vm.CopyLiveRecordDownloadUrlCommand,
					v => v.CopyLiveRecordDownloadUrlMenuItem,
					vm => vm.SelectedItem).DisposeWith(d);
				this.BindCommand(ViewModel,
					vm => vm.OpenDirCommand,
					v => v.OpenDirMenuItem,
					vm => vm.SelectedItem).DisposeWith(d);
				this.BindCommand(ViewModel,
					vm => vm.OpenLiveRecordUrlCommand,
					v => v.OpenLiveRecordUrlMenuItem,
					vm => vm.SelectedItem).DisposeWith(d);
			});
		}
	}
}
