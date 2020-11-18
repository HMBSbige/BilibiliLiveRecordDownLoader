using BilibiliApi.Clients;
using BilibiliApi.Model.LiveRecordList;
using BilibiliLiveRecordDownLoader.Interfaces;
using BilibiliLiveRecordDownLoader.Models;
using BilibiliLiveRecordDownLoader.Shared;
using BilibiliLiveRecordDownLoader.Utils;
using BilibiliLiveRecordDownLoader.ViewModels.TaskViewModels;
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

namespace BilibiliLiveRecordDownLoader.ViewModels
{
#pragma warning disable CS8612
	public class LiveRecordListViewModel : ReactiveObject, IRoutableViewModel, IDisposable
#pragma warning restore CS8612
	{
		public string UrlPathSegment => @"LiveRecordList";
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
		private readonly IConfigService _configService;
		private readonly SourceList<TaskViewModel> _taskSourceList;
		private readonly SourceList<LiveRecordList> _liveRecordSourceList;
		private readonly OperationQueue _liveRecordDownloadTaskQueue;
		public readonly GlobalViewModel Global;

		public readonly ReadOnlyObservableCollection<LiveRecordViewModel> LiveRecordList;
		public Config Config => _configService.Config;
		private const long PageSize = 200;

		public LiveRecordListViewModel(
				IScreen hostScreen,
				ILogger<MainWindowViewModel> logger,
				IConfigService configService,
				SourceList<LiveRecordList> liveRecordSourceList,
				SourceList<TaskViewModel> taskSourceList,
				OperationQueue taskQueue,
				GlobalViewModel global)
		{
			HostScreen = hostScreen;
			_logger = logger;
			_configService = configService;
			_taskSourceList = taskSourceList;
			_liveRecordSourceList = liveRecordSourceList;
			_liveRecordDownloadTaskQueue = taskQueue;
			Global = global;

			_roomIdMonitor = this
					.WhenAnyValue(x => x._configService.Config.RoomId, x => x.Global.TriggerLiveRecordListQuery)
					.Throttle(TimeSpan.FromMilliseconds(800), RxApp.MainThreadScheduler)
					.DistinctUntilChanged()
					.Where(i => i.Item1 > 0)
					.Select(i => i.Item1)
					.Subscribe(i =>
					{
						GetAnchorInfoAsync(i).NoWarning();
						GetRecordListAsync(i).NoWarning();
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

		private static async Task CopyLiveRecordDownloadUrlAsync(object? info)
		{
			try
			{
				if (info is LiveRecordViewModel liveRecord && !string.IsNullOrEmpty(liveRecord.Rid))
				{
					using var client = new BililiveApiClient();
					var message = await client.GetLiveRecordUrlAsync(liveRecord.Rid);
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

		private static IObservable<Unit> OpenLiveRecordUrl(object? info)
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
				catch
				{
					//ignored
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
						var root = Path.Combine(_configService.Config.MainDir, $@"{Global.RoomId}", Constants.LiveRecordPath);
						var path = Path.Combine(root, liveRecord.Rid);
						if (!Utils.Utils.OpenDir(path))
						{
							Directory.CreateDirectory(root);
							Utils.Utils.OpenDir(root);
						}
					}
				}
				catch
				{
					//ignored
				}
			});
		}

		private IObservable<Unit> Download(object? info)
		{
			return Observable.Start(() =>
			{
				try
				{
					if (info is IList { Count: > 0 } list)
					{
						foreach (var item in list)
						{
							if (item is LiveRecordViewModel { Rid: not @"" or null } liveRecord)
							{
								var root = Path.Combine(_configService.Config.MainDir, $@"{Global.RoomId}", Constants.LiveRecordPath);
								var task = new LiveRecordDownloadTaskViewModel(_logger, liveRecord, root, _configService.Config.DownloadThreads);
								if (AddTask(task))
								{
									_liveRecordDownloadTaskQueue.Enqueue(1, Constants.LiveRecordKey, () => task.StartAsync().AsTask()).NoWarning();
								}
							}
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
				using var client = new BililiveApiClient();
				var msg = await client.GetAnchorInfoAsync(roomId);

				if (msg?.data?.info == null || msg.code != 0)
				{
					throw new ArgumentException($@"[{roomId}]获取主播信息出错，可能该房间号的主播不存在");
				}

				var info = msg.data.info;
				Global.ImageUri = info.face;
				Global.Name = info.uname;
				Global.Uid = info.uid;
				Global.Level = info.platform_user_level;
			}
			catch (Exception ex)
			{
				Global.ImageUri = null;
				Global.Name = string.Empty;
				Global.Uid = 0;
				Global.Level = 0;

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
				Global.IsLiveRecordBusy = true;
				Global.RoomId = 0;
				Global.ShortRoomId = 0;
				Global.RecordCount = 0;
				_liveRecordSourceList.Clear();

				using var client = new BililiveApiClient();
				var roomInitMessage = await client.GetRoomInitAsync(roomId);
				if (roomInitMessage != null
					&& roomInitMessage.code == 0
					&& roomInitMessage.data != null
					&& roomInitMessage.data.room_id > 0)
				{
					Global.RoomId = roomInitMessage.data.room_id;
					Global.ShortRoomId = roomInitMessage.data.short_id;
					Global.RecordCount = long.MaxValue;
					var currentPage = 0;
					while (currentPage < Math.Ceiling((double)Global.RecordCount / PageSize))
					{
						var listMessage = await client.GetLiveRecordListAsync(roomInitMessage.data.room_id, ++currentPage, PageSize);
						if (listMessage?.data != null && listMessage.data.count > 0)
						{
							Global.RecordCount = listMessage.data.count;
							var list = listMessage.data?.list;
							if (list != null)
							{
								_liveRecordSourceList.AddRange(list);
							}
						}
						else
						{
							_logger.LogWarning(@"[{0}]加载列表出错，可能该直播间无直播回放", roomId);
							Global.RecordCount = 0;
							break;
						}
					}
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, @"[{0}]加载直播回放列表出错", roomId);
				Global.RecordCount = 0;
			}
			finally
			{
				Global.IsLiveRecordBusy = false;
			}
		}

		public void Dispose()
		{
			_roomIdMonitor.Dispose();
		}
	}
}
