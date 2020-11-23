using BilibiliLiveRecordDownLoader.Enums;
using BilibiliLiveRecordDownLoader.Models;
using BilibiliLiveRecordDownLoader.Shared.Utils;
using BilibiliLiveRecordDownLoader.Views.Dialogs;
using DynamicData;
using Microsoft.Extensions.Logging;
using ModernWpf.Controls;
using ReactiveUI;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
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
		public ReactiveCommand<object?, Unit> ModifyRoomCommand { get; }
		public ReactiveCommand<object?, Unit> RemoveRoomCommand { get; }
		public ReactiveCommand<object?, Unit> RefreshRoomCommand { get; }
		public ReactiveCommand<object?, Unit> OpenDirCommand { get; }
		public ReactiveCommand<object?, Unit> OpenUrlCommand { get; }

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
								case ListChangeReason.AddRange:
								{
									switch (change.Type)
									{
										case ChangeType.Item:
										{
											var room = change.Item.Current;
											room.InitAsync(default).NoWarning();
											break;
										}
										case ChangeType.Range:
										{
											foreach (var room in change.Range)
											{
												room.InitAsync(default).NoWarning();
											}
											break;
										}
									}
									break;
								}
							}
						}
					});

			AddRoomCommand = ReactiveCommand.CreateFromTask(AddRoomAsync);
			ModifyRoomCommand = ReactiveCommand.CreateFromTask<object?, Unit>(ModifyRoomAsync);
			RemoveRoomCommand = ReactiveCommand.CreateFromTask<object?, Unit>(RemoveRoomAsync);
			RefreshRoomCommand = ReactiveCommand.CreateFromTask<object?, Unit>(RefreshRoomAsync);
			OpenDirCommand = ReactiveCommand.CreateFromObservable<object?, Unit>(OpenDir);
			OpenUrlCommand = ReactiveCommand.CreateFromObservable<object?, Unit>(OpenLiveUrl);
		}

		private void RaiseRoomsChanged()
		{
			_config.RaisePropertyChanged(nameof(_config.Rooms));
		}

		private async Task AddRoomAsync(CancellationToken token)
		{
			try
			{
				var room = new RoomStatus();
				using (var dialog = new RoomDialog(RoomDialogType.Add, room))
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
			RaiseRoomsChanged();
		}

		private async Task<Unit> ModifyRoomAsync(object? data, CancellationToken token)
		{
			try
			{
				if (data is not RoomStatus room)
				{
					return default;
				}
				var roomCopy = room.Clone();
				using (var dialog = new RoomDialog(RoomDialogType.Modify, roomCopy))
				{
					if (await dialog.ShowAsync() != ContentDialogResult.Primary)
					{
						return default;
					}
				}
				room.Clone(roomCopy);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, @"修改房间出错");
				using var dialog = new DisposableContentDialog
				{
					Title = @"修改房间出错",
					Content = ex.Message,
					PrimaryButtonText = @"确定",
					DefaultButton = ContentDialogButton.Primary
				};
				await dialog.ShowAsync();
			}
			return default;
		}

		private async Task<Unit> RemoveRoomAsync(object? data, CancellationToken token)
		{
			try
			{
				if (data is not IList { Count: > 0 } list)
				{
					return default;
				}
				var rooms = new List<RoomStatus>();
				foreach (var item in list)
				{
					if (item is not RoomStatus room)
					{
						continue;
					}
					rooms.Add(room);
				}
				var roomList = string.Join('，', rooms.Select(room => string.IsNullOrWhiteSpace(room.UserName) ? $@"{room.RoomId}" : room.UserName));
				using (var dialog = new DisposableContentDialog
				{
					Title = @"确定移除直播间？",
					Content = roomList,
					PrimaryButtonText = @"确定",
					CloseButtonText = @"取消",
					DefaultButton = ContentDialogButton.Close
				})
				{
					if (await dialog.ShowAsync() != ContentDialogResult.Primary)
					{
						return default;
					}
				}
				RemoveRoom(rooms);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, @"删除房间出错");
				using var dialog = new DisposableContentDialog
				{
					Title = @"删除房间出错",
					Content = ex.Message,
					PrimaryButtonText = @"确定",
					DefaultButton = ContentDialogButton.Primary
				};
				await dialog.ShowAsync();
			}
			return default;
		}

		private void RemoveRoom(List<RoomStatus> rooms)
		{
			_roomList.RemoveMany(rooms);
			_config.Rooms.RemoveMany(rooms);
			RaiseRoomsChanged();
		}

		private async Task<Unit> RefreshRoomAsync(object? data, CancellationToken token)
		{
			try
			{
				if (data is not IList { Count: > 0 } list)
				{
					return default;
				}

				foreach (var item in list)
				{
					if (item is not RoomStatus room)
					{
						continue;
					}
					await room.RefreshStatusAsync(token);
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, @"刷新房间状态出错");
			}
			return default;
		}

		private IObservable<Unit> OpenDir(object? data)
		{
			return Observable.Start(() =>
			{
				try
				{
					if (data is RoomStatus room)
					{
						var path = Path.Combine(_config.MainDir, $@"{room.RoomId}");
						if (!Directory.Exists(path))
						{
							Directory.CreateDirectory(path);
						}
						Utils.Utils.OpenDir(path);
					}
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, @"打开目录出错");
				}
			});
		}

		private IObservable<Unit> OpenLiveUrl(object? data)
		{
			return Observable.Start(() =>
			{
				try
				{
					if (data is not IList { Count: > 0 } list)
					{
						return;
					}
					foreach (var item in list)
					{
						if (item is not RoomStatus room)
						{
							continue;
						}
						Utils.Utils.OpenUrl($@"https://live.bilibili.com/{room.RoomId}");
					}
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, @"打开直播间出错");
				}
			});
		}
	}
}
