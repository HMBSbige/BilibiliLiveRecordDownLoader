using BilibiliApi.Clients;
using BilibiliApi.Model.LiveRecordList;
using BilibiliLiveRecordDownLoader.Interfaces;
using BilibiliLiveRecordDownLoader.Shared;
using BilibiliLiveRecordDownLoader.Utils;
using BilibiliLiveRecordDownLoader.ViewModels.TaskViewModels;
using DynamicData;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAPICodePack.Dialogs;
using Punchclock;
using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using UpdateChecker;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace BilibiliLiveRecordDownLoader.ViewModels
{
	public sealed class MainWindowViewModel : ReactiveObject, IDisposable
	{
		#region 字段

		private string? _imageUri;
		private string? _name;
		private long _uid;
		private long _level;
		private string? _diskUsageProgressBarText;
		private double _diskUsageProgressBarValue;
		private long _roomId;
		private long _shortRoomId;
		private long _recordCount;
		private bool _isLiveRecordBusy;
		private bool _triggerLiveRecordListQuery;

		#endregion

		#region 属性

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

		public string? DiskUsageProgressBarText
		{
			get => _diskUsageProgressBarText;
			set => this.RaiseAndSetIfChanged(ref _diskUsageProgressBarText, value);
		}

		public double DiskUsageProgressBarValue
		{
			get => _diskUsageProgressBarValue;
			set => this.RaiseAndSetIfChanged(ref _diskUsageProgressBarValue, value);
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

		private readonly IDisposable _diskMonitor;
		private readonly IDisposable _roomIdMonitor;

		#endregion

		#region Command

		public ReactiveCommand<Unit, Unit> SelectMainDirCommand { get; }
		public ReactiveCommand<Unit, Unit> OpenMainDirCommand { get; }
		public ReactiveCommand<object?, Unit> CopyLiveRecordDownloadUrlCommand { get; }
		public ReactiveCommand<object?, Unit> OpenLiveRecordUrlCommand { get; }
		public ReactiveCommand<object?, Unit> DownLoadCommand { get; }
		public ReactiveCommand<object?, Unit> OpenDirCommand { get; }
		public ReactiveCommand<Unit, Unit> ShowWindowCommand { get; }
		public ReactiveCommand<Unit, Unit> ExitCommand { get; }
		public ReactiveCommand<object?, Unit> StopTaskCommand { get; }
		public ReactiveCommand<Unit, Unit> CheckUpdateCommand { get; }

		#endregion

		private readonly MainWindow _window;
		private readonly ILogger _logger;
		public readonly IConfigService ConfigService;

		private SourceList<LiveRecordList> LiveRecordSourceList { get; } = new();
		public readonly ReadOnlyObservableCollection<LiveRecordListViewModel> LiveRecordList;

		private SourceList<TaskListViewModel> TaskSourceList { get; } = new();
		public readonly ReadOnlyObservableCollection<TaskListViewModel> TaskList;

		private readonly OperationQueue _liveRecordDownloadTaskQueue = new(1);

		private bool _isInitData = true;

		private const long PageSize = 200;

		public MainWindowViewModel(MainWindow window,
			ILogger logger,
			IConfigService configService)
		{
			_window = window;
			_logger = logger;
			ConfigService = configService;

			CheckUpdateCommand = ReactiveCommand.CreateFromTask(CheckUpdateAsync);
			InitAsync().NoWarning();

			_roomIdMonitor = this.WhenAnyValue(x => x.ConfigService.Config.RoomId, x => x.TriggerLiveRecordListQuery)
					.Throttle(TimeSpan.FromMilliseconds(800), RxApp.MainThreadScheduler)
					.DistinctUntilChanged()
					.Where(i => i.Item1 > 0)
					.Select(i => i.Item1)
					.Subscribe(i =>
					{
						GetAnchorInfoAsync(i).NoWarning();
						GetRecordListAsync(i).NoWarning();
					});

			_diskMonitor = Observable.Interval(TimeSpan.FromSeconds(1), RxApp.MainThreadScheduler)
					.Subscribe(GetDiskUsage);

			LiveRecordSourceList.Connect()
					.Transform(x => new LiveRecordListViewModel(x))
					.ObserveOnDispatcher()
					.Bind(out LiveRecordList)
					.DisposeMany()
					.Subscribe(_ =>
					{
						if (!_isInitData)
						{
							return;
						}

						_window.SizeToContent = SizeToContent.Width;
						_window.SizeToContent = SizeToContent.Manual;

						_window.LiveRecordListDataGrid.EnableRowVirtualization = true;

						_isInitData = false;
					});

			TaskSourceList.Connect()
					.ObserveOnDispatcher()
					.Bind(out TaskList)
					.DisposeMany()
					.Subscribe();

			SelectMainDirCommand = ReactiveCommand.Create(SelectDirectory);
			OpenMainDirCommand = ReactiveCommand.CreateFromObservable(OpenDirectory);
			CopyLiveRecordDownloadUrlCommand = ReactiveCommand.CreateFromTask<object?>(CopyLiveRecordDownloadUrlAsync);
			OpenLiveRecordUrlCommand = ReactiveCommand.CreateFromObservable<object?, Unit>(OpenLiveRecordUrl);
			OpenDirCommand = ReactiveCommand.CreateFromObservable<object?, Unit>(OpenDir);
			DownLoadCommand = ReactiveCommand.CreateFromObservable<object?, Unit>(Download);
			ShowWindowCommand = ReactiveCommand.Create(ShowWindow);
			ExitCommand = ReactiveCommand.Create(Exit);
			StopTaskCommand = ReactiveCommand.CreateFromObservable<object?, Unit>(StopTask);
		}

		private async Task InitAsync()
		{
			await ConfigService.LoadAsync(default);
			if (ConfigService.Config.IsCheckUpdateOnStart)
			{
				await CheckUpdateCommand.Execute();
			}
		}

		private async Task CheckUpdateAsync()
		{
			try
			{
				var version = Utils.Utils.GetAppVersion()!;
				var updateChecker = new GitHubReleasesUpdateChecker(
						@"HMBSbige",
						@"BilibiliLiveRecordDownLoader",
						ConfigService.Config.IsCheckPreRelease,
						version
				);
				var res = await updateChecker.CheckAsync(default);
				//TODO
				if (res)
				{
					_logger.LogInformation($@"发现新版本：{updateChecker.LatestVersion}");
				}
				else
				{
					_logger.LogInformation($@"没有找到新版本：{version} ≥ {updateChecker.LatestVersion}");
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, @"检查更新出错");
			}
		}

		private void SelectDirectory()
		{
			var dlg = new CommonOpenFileDialog
			{
				IsFolderPicker = true,
				Multiselect = false,
				Title = @"选择存储目录",
				AddToMostRecentlyUsedList = false,
				EnsurePathExists = true,
				NavigateToShortcut = true,
				InitialDirectory = ConfigService.Config.MainDir
			};
			if (dlg.ShowDialog() == CommonFileDialogResult.Ok)
			{
				ConfigService.Config.MainDir = dlg.FileName;
			}
		}

		private IObservable<Unit> OpenDirectory()
		{
			return Observable.Start(() =>
			{
				Utils.Utils.OpenDir(ConfigService.Config.MainDir);
				return Unit.Default;
			});
		}

		private void GetDiskUsage(long _)
		{
			var (availableFreeSpace, totalSize) = Utils.Utils.GetDiskUsage(ConfigService.Config.MainDir);
			if (totalSize != 0)
			{
				DiskUsageProgressBarText = $@"已使用 {Utils.Utils.CountSize(totalSize - availableFreeSpace)}/{Utils.Utils.CountSize(totalSize)} 剩余 {Utils.Utils.CountSize(availableFreeSpace)}";
				var percentage = (totalSize - availableFreeSpace) / (double)totalSize;
				DiskUsageProgressBarValue = percentage * 100;
			}
			else
			{
				DiskUsageProgressBarText = string.Empty;
				DiskUsageProgressBarValue = 0;
			}
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
				LiveRecordSourceList.Clear();

				using var client = new BililiveApiClient();
				var roomInitMessage = await client.GetRoomInitAsync(roomId);
				if (roomInitMessage != null
					&& roomInitMessage.code == 0
					&& roomInitMessage.data != null
					&& roomInitMessage.data.room_id > 0)
				{
					RoomId = roomInitMessage.data.room_id;
					ShortRoomId = roomInitMessage.data.short_id;
					RecordCount = long.MaxValue;
					var currentPage = 0;
					while (currentPage < Math.Ceiling((double)RecordCount / PageSize))
					{
						var listMessage = await client.GetLiveRecordListAsync(roomInitMessage.data.room_id, ++currentPage, PageSize);
						if (listMessage?.data != null && listMessage.data.count > 0)
						{
							RecordCount = listMessage.data.count;
							var list = listMessage.data?.list;
							if (list != null)
							{
								LiveRecordSourceList.AddRange(list);
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

		private static async Task CopyLiveRecordDownloadUrlAsync(object? info)
		{
			try
			{
				if (info is LiveRecordListViewModel liveRecord && !string.IsNullOrEmpty(liveRecord.Rid))
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
								list.Select(x => x.url is not null or @"" ? x.backup_url : x.url)
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
					if (info is LiveRecordListViewModel { Rid: not @"" or null } liveRecord)
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
					if (info is LiveRecordListViewModel liveRecord && !string.IsNullOrEmpty(liveRecord.Rid))
					{
						var root = Path.Combine(ConfigService.Config.MainDir, $@"{RoomId}", @"Replay");
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
					//if (info is IList { Count: > 0 } list)
					//{
					//
					//}
					if (info is LiveRecordListViewModel { Rid: not @"" or null } liveRecord)
					{
						var root = Path.Combine(ConfigService.Config.MainDir, $@"{RoomId}", @"Replay");
						var task = new LiveRecordDownloadTaskViewModel(_logger, liveRecord, root, ConfigService.Config.DownloadThreads);
						AddTask(task);
					}
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, @"下载回放出错");
				}
			});
		}

		private void AddTask(TaskListViewModel task)
		{
			if (TaskSourceList.Items.Any(x => x.Description == task.Description))
			{
				_logger.LogWarning($@"添加重复任务：{task.Description}");
				return;
			}
			TaskSourceList.Add(task);
			_liveRecordDownloadTaskQueue.Enqueue(1, () => task.StartAsync().AsTask()).NoWarning();
		}

		private IObservable<Unit> StopTask(object? info)
		{
			return Observable.Start(() =>
			{
				try
				{
					if (info is TaskListViewModel task)
					{
						task.Stop();
					}
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, @"停止任务出错");
				}
			});
		}

		private void StopAllTask()
		{
			TaskSourceList.Items.ToList().ForEach(t => t.Stop());
		}

		private void ShowWindow()
		{
			_window.ShowWindow();
		}

		private void Exit()
		{
			StopAllTask();
			_window.CloseReason = CloseReason.ApplicationExitCall;
			_window.Close();
		}

		public void Dispose()
		{
			_diskMonitor.Dispose();
			_roomIdMonitor.Dispose();
			ConfigService.Dispose();
		}
	}
}
