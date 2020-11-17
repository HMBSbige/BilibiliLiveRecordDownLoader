using BilibiliApi.Clients;
using BilibiliApi.Model.LiveRecordList;
using BilibiliLiveRecordDownLoader.Interfaces;
using BilibiliLiveRecordDownLoader.Shared;
using BilibiliLiveRecordDownLoader.Utils;
using BilibiliLiveRecordDownLoader.ViewModels.TaskViewModels;
using DynamicData;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAPICodePack.Dialogs;
using ModernWpf.Controls;
using Punchclock;
using ReactiveUI;
using Splat;
using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
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
		private string? _updateStatus;

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

		public string? UpdateStatus
		{
			get => _updateStatus;
			set => this.RaiseAndSetIfChanged(ref _updateStatus, value);
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
		public ReactiveCommand<Unit, Unit> ClearAllTasksCommand { get; }

		#endregion

		private readonly ILogger _logger;
		public readonly IConfigService ConfigService;
		private readonly SourceList<LiveRecordList> _liveRecordSourceList;
		private readonly SourceList<TaskListViewModel> _taskSourceList;
		private readonly OperationQueue _liveRecordDownloadTaskQueue;

		public readonly ReadOnlyObservableCollection<LiveRecordListViewModel> LiveRecordList;
		public readonly ReadOnlyObservableCollection<TaskListViewModel> TaskList;
		private const long PageSize = 200;

		public MainWindowViewModel(
			ILogger<MainWindowViewModel> logger,
			IConfigService configService,
			SourceList<LiveRecordList> liveRecordSourceList,
			SourceList<TaskListViewModel> taskSourceList,
			OperationQueue taskQueue)
		{
			_logger = logger;
			ConfigService = configService;
			_liveRecordSourceList = liveRecordSourceList;
			_taskSourceList = taskSourceList;
			_liveRecordDownloadTaskQueue = taskQueue;

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

			_liveRecordSourceList.Connect()
					.Transform(x => new LiveRecordListViewModel(x))
					.ObserveOnDispatcher()
					.Bind(out LiveRecordList)
					.DisposeMany()
					.Subscribe();

			_taskSourceList.Connect()
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
			ClearAllTasksCommand = ReactiveCommand.CreateFromTask(ClearAllTasksAsync);
		}

		private async ValueTask InitAsync()
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
				UpdateStatus = @"正在检查更新...";
				var version = Utils.Utils.GetAppVersion()!;
				var updateChecker = new GitHubReleasesUpdateChecker(
						@"HMBSbige",
						@"BilibiliLiveRecordDownLoader",
						ConfigService.Config.IsCheckPreRelease,
						version
				);
				if (await updateChecker.CheckAsync(default))
				{
					if (updateChecker.LatestVersionUrl is null)
					{
						UpdateStatus = @"更新地址获取出错";
						return;
					}

					UpdateStatus = $@"发现新版本：{updateChecker.LatestVersion}";
					using var dialog = new DisposableContentDialog
					{
						Title = UpdateStatus,
						Content = @"是否跳转到下载页？",
						PrimaryButtonText = @"是",
						SecondaryButtonText = @"否",
						DefaultButton = ContentDialogButton.Primary
					};
					if (await dialog.ShowAsync() == ContentDialogResult.Primary)
					{
						Utils.Utils.OpenUrl(updateChecker.LatestVersionUrl);
					}
				}
				else
				{
					UpdateStatus = $@"没有找到新版本：{version} ≥ {updateChecker.LatestVersion}";
				}
			}
			catch (Exception ex)
			{
				UpdateStatus = @"检查更新出错";
				_logger.LogError(ex, UpdateStatus);
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
				_liveRecordSourceList.Clear();

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
						var root = Path.Combine(ConfigService.Config.MainDir, $@"{RoomId}", Constants.LiveRecordPath);
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
							if (item is LiveRecordListViewModel { Rid: not @"" or null } liveRecord)
							{
								var root = Path.Combine(ConfigService.Config.MainDir, $@"{RoomId}", Constants.LiveRecordPath);
								var task = new LiveRecordDownloadTaskViewModel(_logger, liveRecord, root, ConfigService.Config.DownloadThreads);
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

		private bool AddTask(TaskListViewModel task)
		{
			if (_taskSourceList.Items.Any(x => x.Description == task.Description))
			{
				_logger.LogWarning($@"添加重复任务：{task.Description}");
				return false;
			}
			_taskSourceList.Add(task);
			return true;
		}

		private IObservable<Unit> StopTask(object? info)
		{
			return Observable.Start(() =>
			{
				try
				{
					if (info is IList { Count: > 0 } list)
					{
						foreach (var item in list)
						{
							if (item is TaskListViewModel task)
							{
								task.Stop();
								_taskSourceList.Remove(task);
							}
						}
					}
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, @"停止任务出错");
				}
			});
		}

		private async Task ClearAllTasksAsync()
		{
			try
			{
				if (_taskSourceList.Count == 0)
				{
					return;
				}
				using var dialog = new DisposableContentDialog
				{
					Title = @"确定清空所有任务？",
					Content = @"将会停止所有任务并清空列表",
					PrimaryButtonText = @"确定",
					CloseButtonText = @"取消",
					DefaultButton = ContentDialogButton.Primary
				};
				if (await dialog.ShowAsync() == ContentDialogResult.Primary)
				{
					_taskSourceList.Items.ToList().ForEach(task =>
					{
						task.Stop();
						_taskSourceList.Remove(task);
					});
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, @"停止任务出错");
			}
		}

		private void StopAllTask()
		{
			_taskSourceList.Items.ToList().ForEach(t => t.Stop());
		}

		private static void ShowWindow()
		{
			Locator.Current.GetService<MainWindow>().ShowWindow();
		}

		private void Exit()
		{
			StopAllTask();
			var window = Locator.Current.GetService<MainWindow>();
			window.CloseReason = CloseReason.ApplicationExitCall;
			window.Close();
		}

		public void Dispose()
		{
			_diskMonitor.Dispose();
			_roomIdMonitor.Dispose();
			ConfigService.Dispose();
		}
	}
}
