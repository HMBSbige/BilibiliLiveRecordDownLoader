using BilibiliLiveRecordDownLoader.Enums;
using BilibiliLiveRecordDownLoader.Models;
using BilibiliLiveRecordDownLoader.Utils;
using BilibiliLiveRecordDownLoader.Views.Dialogs;
using DynamicData;
using Microsoft.Extensions.Logging;
using ModernWpf.Controls;
using ReactiveUI;
using System.Collections;
using System.Collections.ObjectModel;
using System.IO;
using System.Reactive;
using System.Reactive.Linq;
using System.Text.Json;

namespace BilibiliLiveRecordDownLoader.ViewModels;

public class StreamRecordViewModel : ReactiveObject, IRoutableViewModel
{
	public string UrlPathSegment => @"StreamRecord";
	public IScreen HostScreen { get; }

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
			.ObserveOn(RxApp.MainThreadScheduler)
			.Bind(out RoomList)
			.DisposeMany()
			.Subscribe(RoomListChanged);

		AddRoomCommand = ReactiveCommand.CreateFromTask(AddRoomAsync);
		ModifyRoomCommand = ReactiveCommand.CreateFromTask<object?, Unit>(ModifyRoomAsync);
		RemoveRoomCommand = ReactiveCommand.CreateFromTask<object?, Unit>(RemoveRoomAsync);
		RefreshRoomCommand = ReactiveCommand.CreateFromTask<object?, Unit>(RefreshRoomAsync);
		OpenDirCommand = ReactiveCommand.CreateFromObservable<object?, Unit>(OpenDir);
		OpenUrlCommand = ReactiveCommand.CreateFromObservable<object?, Unit>(OpenLiveUrl);
	}

	private static void RoomListChanged(IChangeSet<RoomStatus> changeSet)
	{
		foreach (Change<RoomStatus> change in changeSet)
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
							RoomStatus room = change.Item.Current;
							room.Start();
							break;
						}
						case ChangeType.Range:
						{
							foreach (RoomStatus room in change.Range)
							{
								room.Start();
							}

							break;
						}
					}

					break;
				}
				case ListChangeReason.Remove:
				case ListChangeReason.RemoveRange:
				case ListChangeReason.Clear:
				{
					switch (change.Type)
					{
						case ChangeType.Item:
						{
							RoomStatus room = change.Item.Current;
							room.Stop();
							break;
						}
						case ChangeType.Range:
						{
							foreach (RoomStatus room in change.Range)
							{
								room.Stop();
							}

							break;
						}
					}

					break;
				}
			}
		}
	}

	private void RaiseRoomsChanged()
	{
		_config.RaisePropertyChanged(nameof(_config.Rooms));
	}

	private async Task AddRoomAsync(CancellationToken token)
	{
		try
		{
			RoomStatus room = new();
			using (RoomDialog dialog = new(RoomDialogType.Add, room))
			{
				if (await dialog.SafeShowAsync() != ContentDialogResult.Primary)
				{
					return;
				}
			}

			await room.GetRoomInfoDataAsync(token);

			if (_roomList.Items.Any(x => x.RoomId == room.RoomId))
			{
				using DisposableContentDialog dialog = new();
				dialog.Title = @"房间已存在";
				dialog.Content = @"不能添加重复房间";
				dialog.PrimaryButtonText = @"确定";
				dialog.DefaultButton = ContentDialogButton.Primary;
				await dialog.SafeShowAsync();
				return;
			}

			AddRoom(room);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, @"添加房间出错");
			string message = ex is JsonException ? @"可能是房间号错误" : ex.Message;
			using DisposableContentDialog dialog = new();
			dialog.Title = @"添加房间出错";
			dialog.Content = message;
			dialog.PrimaryButtonText = @"确定";
			dialog.DefaultButton = ContentDialogButton.Primary;
			await dialog.SafeShowAsync();
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
			RoomStatus roomCopy = room.Clone();
			using (RoomDialog dialog = new(RoomDialogType.Modify, roomCopy))
			{
				if (await dialog.SafeShowAsync() != ContentDialogResult.Primary)
				{
					return default;
				}
			}
			room.Update(roomCopy);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, @"修改房间出错");
			using DisposableContentDialog dialog = new();
			dialog.Title = @"修改房间出错";
			dialog.Content = ex.Message;
			dialog.PrimaryButtonText = @"确定";
			dialog.DefaultButton = ContentDialogButton.Primary;
			await dialog.SafeShowAsync();
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
			List<RoomStatus> rooms = new();
			foreach (object? item in list)
			{
				if (item is not RoomStatus room)
				{
					continue;
				}
				rooms.Add(room);
			}
			string roomList = string.Join('，', rooms.Select(room => string.IsNullOrWhiteSpace(room.UserName) ? $@"{room.RoomId}" : room.UserName));
			using (DisposableContentDialog dialog = new())
			{
				dialog.Title = @"确定移除直播间？";
				dialog.Content = roomList;
				dialog.PrimaryButtonText = @"确定";
				dialog.CloseButtonText = @"取消";
				dialog.DefaultButton = ContentDialogButton.Close;
				if (await dialog.SafeShowAsync() != ContentDialogResult.Primary)
				{
					return default;
				}
			}
			RemoveRoom(rooms);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, @"删除房间出错");
			using DisposableContentDialog dialog = new();
			dialog.Title = @"删除房间出错";
			dialog.Content = ex.Message;
			dialog.PrimaryButtonText = @"确定";
			dialog.DefaultButton = ContentDialogButton.Primary;
			await dialog.SafeShowAsync();
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

			foreach (object? item in list)
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
				if (data is not RoomStatus room)
				{
					return;
				}

				string path = Path.Combine(_config.MainDir, $@"{room.RoomId}");
				Directory.CreateDirectory(path);
				FileUtils.OpenDir(path);
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
				foreach (object? item in list)
				{
					if (item is not RoomStatus room)
					{
						continue;
					}
					FileUtils.OpenUrl($@"https://live.bilibili.com/{room.RoomId}");
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, @"打开直播间出错");
			}
		});
	}
}
