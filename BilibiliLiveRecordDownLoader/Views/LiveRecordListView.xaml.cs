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
						ViewModel.Global.TriggerLiveRecordListQuery = !ViewModel.Global.TriggerLiveRecordListQuery;
					}
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

				this.BindCommand(ViewModel, vm => vm.CopyLiveRecordDownloadUrlCommand, v => v.CopyLiveRecordDownloadUrlMenuItem)
				//TODO .DisposeWith(d)
				;
				this.BindCommand(ViewModel, vm => vm.OpenLiveRecordUrlCommand, v => v.OpenLiveRecordUrlMenuItem)
				//TODO .DisposeWith(d)
				;
				this.BindCommand(ViewModel, vm => vm.DownLoadCommand, v => v.DownLoadMenuItem)
				//TODO .DisposeWith(d)
				;
				this.BindCommand(ViewModel, vm => vm.OpenDirCommand, v => v.OpenDirMenuItem)
				//TODO .DisposeWith(d)
				;
			});
		}
	}
}
