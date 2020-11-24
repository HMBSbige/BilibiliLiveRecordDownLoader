using BilibiliApi.Clients;
using BilibiliApi.Model.LiveRecordList;
using BilibiliLiveRecordDownLoader.Models;
using BilibiliLiveRecordDownLoader.Models.TaskViewModels;
using BilibiliLiveRecordDownLoader.Utils;
using DynamicData;
using Microsoft.Extensions.Logging;
using Punchclock;
using ReactiveUI;
using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Extensions = BilibiliLiveRecordDownLoader.Shared.Utils.Extensions;

namespace BilibiliLiveRecordDownLoader.ViewModels
{
#pragma warning disable CS8612
	public sealed class LiveRecordListViewModel : ReactiveObject, IRoutableViewModel, IDisposable
#pragma warning restore CS8612
	{
		public string UrlPathSegment => @"LiveRecordList";
		public IScreen HostScreen { get; }

		#region 字段

		private object? _selectedItem;
		private object? _selectedItems;
		private string? _imageUri;
		private string? _name;
		private long _uid;
		private long _level;
		private long _roomId;
		private long _shortRoomId;
		private long _recordCount;
		private bool _isLiveRecordBusy;
		private bool _triggerLiveRecordListQuery;

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

		public string? ImageUri
		{
			get => _imageUri;
			set => this.RaiseAndSetIfChanged(ref _imageUri, value);
		}

		public string? Name
		{
			get => _name;
			set => this.RaiseAndSetIfChanged(ref _name, value);
		}

		public long Uid
		{
			get => _uid;
			set => this.RaiseAndSetIfChanged(ref _uid, value);
		}

		public long Level
		{
			get => _level;
			set => this.RaiseAndSetIfChanged(ref _level, value);
		}

		public long RoomId
		{
			get => _roomId;
			set => this.RaiseAndSetIfChanged(ref _roomId, value);
		}

		public long ShortRoomId
		{
			get => _shortRoomId;
			set => this.RaiseAndSetIfChanged(ref _shortRoomId, value);
		}

		public long RecordCount
		{
			get => _recordCount;
			set => this.RaiseAndSetIfChanged(ref _recordCount, value);
		}

		public bool IsLiveRecordBusy
		{
			get => _isLiveRecordBusy;
			set => this.RaiseAndSetIfChanged(ref _isLiveRecordBusy, value);
		}

		public bool TriggerLiveRecordListQuery
		{
			get => _triggerLiveRecordListQuery;
			set => this.RaiseAndSetIfChanged(ref _triggerLiveRecordListQuery, value);
		}

		#endregion

		#region Monitor

		private readonly IDisposable _roomIdMonitor;

		#endregion

		#region Command

		public ReactiveCommand<object?, Unit> CopyLiveRecordDownloadUrlCommand { get; }
		public ReactiveCommand<object?, Unit> OpenLiveRecordUrlCommand { get; }
		public ReactiveCommand<object?, Unit> DownLoadCommand { get; }
		public ReactiveCommand<object?, Unit> OpenDirCommand { get; }

		#endregion

		private readonly ILogger _logger;
		private readonly SourceList<TaskViewModel> _taskSourceList;
		private readonly SourceList<LiveRecordList> _liveRecordSourceList;
		private readonly OperationQueue _liveRecordDownloadTaskQueue;
		private readonly BililiveApiClient _apiClient;

		public readonly ReadOnlyObservableCollection<LiveRecordViewModel> LiveRecordList;
		public readonly Config Config;
		private const long PageSize = 200;

		public LiveRecordListViewModel(
				IScreen hostScreen,
				ILogger<LiveRecordListViewModel> logger,
				Config config,
				SourceList<LiveRecordList> liveRecordSourceList,
				SourceList<TaskViewModel> taskSourceList,
				OperationQueue taskQueue,
				BililiveApiClient apiClient)
		{
			HostScreen = hostScreen;
			_logger = logger;
			Config = config;
			_taskSourceList = taskSourceList;
			_liveRecordSourceList = liveRecordSourceList;
			_liveRecordDownloadTaskQueue = taskQueue;
			_apiClient = apiClient;

			_roomIdMonitor = this
					.WhenAnyValue(x => x.Config.RoomId, x => x.TriggerLiveRecordListQuery)
					.Throttle(TimeSpan.FromMilliseconds(800), RxApp.MainThreadScheduler)
					.DistinctUntilChanged()
					.Where(i => i.Item1 > 0)
					.Select(i => i.Item1)
					.Subscribe(i =>
					{
						Extensions.NoWarning(GetAnchorInfoAsync(i));
						Extensions.NoWarning(GetRecordListAsync(i));
					});

			_liveRecordSourceList.Connect()
					.Transform(x => new LiveRecordViewModel(x))
					.ObserveOnDispatcher()
					.Bind(out LiveRecordList)
					.DisposeMany()
					.Subscribe();

			CopyLiveRecordDownloadUrlCommand = ReactiveCommand.CreateFromTask<object?>(CopyLiveRecordDownloadUrlAsync);
			OpenLiveRecordUrlCommand = ReactiveCommand.CreateFromObservable<object?, Unit>(OpenLiveRecordUrl);
			OpenDirCommand = ReactiveCommand.CreateFromObservable<object?, Unit>(OpenDir);
			DownLoadCommand = ReactiveCommand.CreateFromObservable<object?, Unit>(Download);
		}

		private async Task CopyLiveRecordDownloadUrlAsync(object? info)
		{
			try
			{
				if (info is LiveRecordViewModel liveRecord && !string.IsNullOrEmpty(liveRecord.Rid))
				{
					var message = await _apiClient.GetLiveRecordUrlAsync(liveRecord.Rid);
					var list = message?.data?.list;
					if (list is not null
						&& list.Length > 0
						&& list.All(x => x.url is not null or @"" || x.backup_url is not null or @"")
						)
					{
						Utils.Utils.CopyToClipboard(string.Join(Environment.NewLine,
								list.Select(x => x.url is null or @"" ? x.backup_url : x.url)
						));
					}
				}
			}
			catch
			{
				//ignored
			}
		}

		private IObservable<Unit> OpenLiveRecordUrl(object? info)
		{
			return Observable.Start(() =>
			{
				try
				{
					if (info is LiveRecordViewModel { Rid: not @"" or null } liveRecord)
					{
						Utils.Utils.OpenUrl($@"https://live.bilibili.com/record/{liveRecord.Rid}");
					}
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, @"打开地址出错");
				}
			});
		}

		private IObservable<Unit> OpenDir(object? info)
		{
			return Observable.Start(() =>
			{
				try
				{
					if (info is LiveRecordViewModel liveRecord && !string.IsNullOrEmpty(liveRecord.Rid))
					{
						var root = Path.Combine(Config.MainDir, $@"{RoomId}", Constants.LiveRecordPath);
						var path = Path.Combine(root, liveRecord.Rid);
						if (!Utils.Utils.OpenDir(path))
						{
							Directory.CreateDirectory(root);
							Utils.Utils.OpenDir(root);
						}
					}
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, @"打开目录出错");
				}
			});
		}

		private IObservable<Unit> Download(object? info)
		{
			return Observable.Start(() =>
			{
				try
				{
					if (info is not IList { Count: > 0 } list)
					{
						return;
					}

					foreach (var item in list)
					{
						if (item is not LiveRecordViewModel { Rid: not @"" or null } liveRecord)
						{
							continue;
						}

						var root = Path.Combine(Config.MainDir, $@"{RoomId}", Constants.LiveRecordPath);
						var task = new LiveRecordDownloadTaskViewModel(liveRecord, root, Config.DownloadThreads);
						if (AddTask(task))
						{
							Extensions.NoWarning(_liveRecordDownloadTaskQueue.Enqueue(1, Constants.LiveRecordKey, () => task.StartAsync().AsTask()));
						}
					}
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, @"下载回放出错");
				}
			});
		}

		private bool AddTask(TaskViewModel task)
		{
			if (_taskSourceList.Items.Any(x => x.Description == task.Description))
			{
				_logger.LogWarning($@"添加重复任务：{task.Description}");
				return false;
			}
			_taskSourceList.Add(task);
			return true;
		}

		private async Task GetAnchorInfoAsync(long roomId)
		{
			try
			{
				var info = await _apiClient.GetAnchorInfoDataAsync(roomId);

				ImageUri = info.face;
				Name = info.uname;
				Uid = info.uid;
				Level = info.platform_user_level;
			}
			catch (Exception ex)
			{
				ImageUri = null;
				Name = string.Empty;
				Uid = 0;
				Level = 0;

				if (ex is ArgumentException)
				{
					_logger.LogWarning(ex.Message);
				}
				else
				{
					_logger.LogError(ex, @"[{0}]获取主播信息出错", roomId);
				}
			}
		}

		private async Task GetRecordListAsync(long roomId)
		{
			try
			{
				IsLiveRecordBusy = true;
				RoomId = 0;
				ShortRoomId = 0;
				RecordCount = 0;
				_liveRecordSourceList.Clear();

				var roomInitMessage = await _apiClient.GetRoomInitAsync(roomId);
				if (roomInitMessage?.data is not null
					&& roomInitMessage.code == 0
					&& roomInitMessage.data.room_id > 0)
				{
					RoomId = roomInitMessage.data.room_id;
					ShortRoomId = roomInitMessage.data.short_id;
					RecordCount = long.MaxValue;
					var currentPage = 0;
					while (currentPage < Math.Ceiling((double)RecordCount / PageSize))
					{
						var listMessage = await _apiClient.GetLiveRecordListAsync(roomInitMessage.data.room_id, ++currentPage, PageSize);
						if (listMessage?.data is not null && listMessage.data.count > 0)
						{
							RecordCount = listMessage.data.count;
							var list = listMessage.data?.list;
							if (list is not null)
							{
								_liveRecordSourceList.AddRange(list);
							}
						}
						else
						{
							_logger.LogWarning(@"[{0}]加载列表出错，可能该直播间无直播回放", roomId);
							RecordCount = 0;
							break;
						}
					}
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, @"[{0}]加载直播回放列表出错", roomId);
				RecordCount = 0;
			}
			finally
			{
				IsLiveRecordBusy = false;
			}
		}

		public void Dispose()
		{
			_roomIdMonitor.Dispose();
		}
	}
}
