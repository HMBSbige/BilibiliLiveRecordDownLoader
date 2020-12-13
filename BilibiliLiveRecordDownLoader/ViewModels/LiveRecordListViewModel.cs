using BilibiliApi.Clients;
using BilibiliApi.Model.LiveRecordList;
using BilibiliLiveRecordDownLoader.Models;
using BilibiliLiveRecordDownLoader.Models.TaskViewModels;
using BilibiliLiveRecordDownLoader.Shared.Utils;
using BilibiliLiveRecordDownLoader.Utils;
using DynamicData;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
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
	public sealed class LiveRecordListViewModel : ReactiveObject, IRoutableViewModel, IDisposable
	{
		public string UrlPathSegment => @"LiveRecordList";
		public IScreen HostScreen { get; }

		#region 属性

		[Reactive]
		public string? ImageUri { get; set; }

		[Reactive]
		public string? Name { get; set; }

		[Reactive]
		public long Uid { get; set; }

		[Reactive]
		public long Level { get; set; }

		[Reactive]
		public long RoomId { get; set; }

		[Reactive]
		public long ShortRoomId { get; set; }

		[Reactive]
		public long RecordCount { get; set; }

		[Reactive]
		public bool IsLiveRecordBusy { get; set; }

		[Reactive]
		public bool TriggerLiveRecordListQuery { get; set; }

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
		private readonly TaskListViewModel _taskList;
		private readonly SourceList<LiveRecordList> _liveRecordSourceList;
		private readonly BililiveApiClient _apiClient;

		public readonly ReadOnlyObservableCollection<LiveRecordViewModel> LiveRecordList;
		public readonly Config Config;
		private const long PageSize = 200;

		public LiveRecordListViewModel(
				IScreen hostScreen,
				ILogger<LiveRecordListViewModel> logger,
				Config config,
				SourceList<LiveRecordList> liveRecordSourceList,
				TaskListViewModel taskList,
				BililiveApiClient apiClient)
		{
			HostScreen = hostScreen;
			_logger = logger;
			Config = config;
			_taskList = taskList;
			_liveRecordSourceList = liveRecordSourceList;
			_apiClient = apiClient;

			_roomIdMonitor = this
					.WhenAnyValue(x => x.Config.RoomId, x => x.TriggerLiveRecordListQuery)
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
						&& list.All(x => !string.IsNullOrEmpty(x.url) || !string.IsNullOrEmpty(x.backup_url))
						)
					{
						Utils.Utils.CopyToClipboard(string.Join(Environment.NewLine,
								list.Select(x => string.IsNullOrEmpty(x.url) ? x.backup_url : x.url)
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
					if (info is LiveRecordViewModel { Rid: not null and not @"" } liveRecord)
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
						if (item is not LiveRecordViewModel { Rid: not null and not @"" } liveRecord)
						{
							continue;
						}

						var root = Path.Combine(Config.MainDir, $@"{RoomId}", Constants.LiveRecordPath);
						var task = new LiveRecordDownloadTaskViewModel(liveRecord, root, Config.DownloadThreads);
						_taskList.AddTaskAsync(task, Constants.LiveRecordKey).NoWarning();
					}
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, @"下载回放出错");
				}
			});
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
