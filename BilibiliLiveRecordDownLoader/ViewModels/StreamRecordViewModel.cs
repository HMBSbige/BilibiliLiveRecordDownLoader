using BilibiliLiveRecordDownLoader.Models;
using BilibiliLiveRecordDownLoader.Shared.Utils;
using BilibiliLiveRecordDownLoader.Views.Dialogs;
using DynamicData;
using Microsoft.Extensions.Logging;
using ModernWpf.Controls;
using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BilibiliLiveRecordDownLoader.ViewModels
{
#pragma warning disable CS8612
	public class StreamRecordViewModel : ReactiveObject, IRoutableViewModel
#pragma warning restore CS8612
	{
		public string UrlPathSegment => @"StreamRecord";
		public IScreen HostScreen { get; }

		#region 字段

		private object? _selectedItem;
		private object? _selectedItems;

		#endregion

		#region 属性

		public object? SelectedItem
		{
			get => _selectedItem;
			set => this.RaiseAndSetIfChanged(ref _selectedItem, value);
		}

		public object? SelectedItems
		{
			get => _selectedItems;
			set => this.RaiseAndSetIfChanged(ref _selectedItems, value);
		}

		#endregion

		#region Command

		public ReactiveCommand<Unit, Unit> AddRoomCommand { get; }

		#endregion

		private readonly ILogger _logger;
		private readonly SourceList<RoomStatus> _roomList;
		private readonly Config _config;

		public readonly ReadOnlyObservableCollection<RoomStatus> RoomList;

		public StreamRecordViewModel(
			IScreen hostScreen,
			ILogger<StreamRecordViewModel> logger,
			SourceList<RoomStatus> roomList,
			Config config)
		{
			HostScreen = hostScreen;
			_logger = logger;
			_roomList = roomList;
			_config = config;

			_roomList.Connect()
					.ObserveOnDispatcher()
					.Bind(out RoomList)
					.DisposeMany()
					.Subscribe(changeSet =>
					{
						foreach (var change in changeSet)
						{
							switch (change.Reason)
							{
								case ListChangeReason.Add:
								{
									var room = change.Item.Current;
									room.InitAsync(default).NoWarning();
									break;
								}
								case ListChangeReason.AddRange:
								{
									foreach (var room in change.Range)
									{
										room.InitAsync(default).NoWarning();
									}
									break;
								}
							}
						}
					});

			AddRoomCommand = ReactiveCommand.CreateFromTask(AddRoomAsync);
		}

		private async Task AddRoomAsync(CancellationToken token)
		{
			try
			{
				var room = new RoomStatus();
				using (var dialog = new RoomDialog(room) { Title = @"添加房间" })
				{
					if (await dialog.ShowAsync() != ContentDialogResult.Primary)
					{
						return;
					}
				}

				await room.GetRoomInfoDataAsync(true, token);

				if (_roomList.Items.Any(x => x.RoomId == room.RoomId))
				{
					using var dialog = new DisposableContentDialog
					{
						Title = @"房间已存在",
						Content = @"不能添加重复房间",
						PrimaryButtonText = @"确定",
						DefaultButton = ContentDialogButton.Primary
					};
					await dialog.ShowAsync();
					return;
				}

				AddRoom(room);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, @"添加房间出错");
				var message = ex is JsonException ? @"可能是房间号错误" : ex.Message;
				using var dialog = new DisposableContentDialog
				{
					Title = @"添加房间出错",
					Content = message,
					PrimaryButtonText = @"确定",
					DefaultButton = ContentDialogButton.Primary
				};
				await dialog.ShowAsync();
			}
		}

		private void AddRoom(RoomStatus room)
		{
			_roomList.Add(room);
			_config.Rooms.Add(room);
			_config.RaisePropertyChanged(nameof(_config.Rooms));
		}
	}
}
