using BilibiliLiveRecordDownLoader.BilibiliApi;
using BilibiliLiveRecordDownLoader.BilibiliApi.Model;
using BilibiliLiveRecordDownLoader.Interfaces;
using BilibiliLiveRecordDownLoader.Services;
using BilibiliLiveRecordDownLoader.Utils;
using DynamicData;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAPICodePack.Dialogs;
using ReactiveUI;
using Syncfusion.Data.Extensions;
using Syncfusion.UI.Xaml.Grid;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;

namespace BilibiliLiveRecordDownLoader.ViewModels
{
    public class MainWindowViewModel : ReactiveObject, IDisposable
    {
        #region 字段

        private string _imageUri;
        private string _name;
        private long _uid;
        private long _level;
        private string _diskUsageProgressBarText;
        private double _diskUsageProgressBarValue;
        private long _roomId;
        private long _shortRoomId;
        private long _recordCount;
        private bool _isLiveRecordBusy;
        private bool _triggerLiveRecordListQuery;

        #endregion

        #region 属性

        public string ImageUri
        {
            get => _imageUri;
            set => this.RaiseAndSetIfChanged(ref _imageUri, value);
        }

        public string Name
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

        public string DiskUsageProgressBarText
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
        public ReactiveCommand<GridRecordContextMenuInfo, Unit> CopyLiveRecordDownloadUrlCommand { get; }
        public ReactiveCommand<GridRecordContextMenuInfo, Unit> OpenLiveRecordUrlCommand { get; }
        public ReactiveCommand<GridRecordContextMenuInfo, Unit> DownLoadCommand { get; }
        public ReactiveCommand<GridRecordContextMenuInfo, Unit> OpenDirCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowWindowCommand { get; }
        public ReactiveCommand<Unit, Unit> ExitCommand { get; }
        #endregion

        private readonly MainWindow _window;
        private readonly ILogger _logger;
        public readonly IConfigService ConfigService;

        public readonly DownloadTaskPool DownloadTaskPool = new DownloadTaskPool();

        private SourceList<LiveRecordList> LiveRecordSourceList { get; } = new SourceList<LiveRecordList>();
        public readonly ReadOnlyObservableCollection<LiveRecordListViewModel> LiveRecordList;

        private bool _isInitData = true;

        public MainWindowViewModel(MainWindow window,
            ILogger logger,
            IConfigService configService)
        {
            _window = window;
            _logger = logger;
            ConfigService = configService;

            InitAsync().NoWarning();

            _roomIdMonitor = this.WhenAnyValue(x => x.ConfigService.Config.RoomId, x => x.TriggerLiveRecordListQuery)
                    .Throttle(TimeSpan.FromMilliseconds(800))
                    .DistinctUntilChanged()
                    .Where(i => i.Item1 > 0)
                    .Select(i => i.Item1)
                    .ObserveOn(RxApp.MainThreadScheduler)
                    .Subscribe(i => GetAnchorInfoAsync(i).NoWarning());

            _diskMonitor = Observable.Interval(TimeSpan.FromSeconds(1))
                    .ObserveOn(RxApp.MainThreadScheduler)
                    .Subscribe(GetDiskUsage);

            LiveRecordSourceList.Connect()
                    .Transform(x =>
                    {
                        var record = new LiveRecordListViewModel(x);
                        DownloadTaskPool.Attach(record);
                        return record;
                    })
                    .ObserveOnDispatcher()
                    .Bind(out LiveRecordList)
                    .DisposeMany()
                    .Subscribe(_ =>
                    {
                        var dataGrid = _window.LiveRecordListDataGrid;
                        dataGrid.GridColumnSizer.ResetAutoCalculationforAllColumns();
                        dataGrid.Columns.ForEach(c => c.Width = double.NaN);
                        dataGrid.GridColumnSizer.Refresh();

                        if (!_isInitData)
                        {
                            return;
                        }

                        _window.SizeToContent = SizeToContent.Width;
                        _window.SizeToContent = SizeToContent.Manual;
                        _isInitData = false;
                    });

            this.WhenAnyValue(x => x.ConfigService.Config.RoomId, x => x.TriggerLiveRecordListQuery)
                .Throttle(TimeSpan.FromMilliseconds(800))
                .DistinctUntilChanged()
                .Where(i => i.Item1 > 0)
                .Select(i => i.Item1)
                .Subscribe(i => GetRecordListAsync(i).NoWarning());

            SelectMainDirCommand = ReactiveCommand.Create(SelectDirectory);
            OpenMainDirCommand = ReactiveCommand.CreateFromObservable(OpenDirectory);
            CopyLiveRecordDownloadUrlCommand = ReactiveCommand.CreateFromTask<GridRecordContextMenuInfo>(CopyLiveRecordDownloadUrlAsync);
            OpenLiveRecordUrlCommand = ReactiveCommand.CreateFromObservable<GridRecordContextMenuInfo, Unit>(OpenLiveRecordUrl);
            OpenDirCommand = ReactiveCommand.CreateFromObservable<GridRecordContextMenuInfo, Unit>(OpenDir);
            DownLoadCommand = ReactiveCommand.CreateFromObservable<GridRecordContextMenuInfo, Unit>(Download);
            ShowWindowCommand = ReactiveCommand.Create(ShowWindow);
            ExitCommand = ReactiveCommand.Create(Exit);
        }

        private async Task InitAsync()
        {
            await ConfigService.LoadAsync(default);
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
                var msg = await client.GetAnchorInfo(roomId);

                if (msg.code != 0 || msg.data?.info == null)
                {
                    return;
                }

                var info = msg.data.info;
                ImageUri = info.face;
                Name = info.uname;
                Uid = info.uid;
                Level = info.platform_user_level;
            }
            catch
            {
                // ignored
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
                var roomInitMessage = await client.GetRoomInit(roomId);
                if (roomInitMessage != null
                    && roomInitMessage.code == 0
                    && roomInitMessage.data != null
                    && roomInitMessage.data.room_id > 0)
                {
                    RoomId = roomInitMessage.data.room_id;
                    ShortRoomId = roomInitMessage.data.short_id;
                    var listMessage = await client.GetLiveRecordList(roomInitMessage.data.room_id, 1, 1);
                    if (listMessage?.data != null && listMessage.data.count > 0)
                    {
                        var count = listMessage.data.count;
                        RecordCount = count;
                        listMessage = await client.GetLiveRecordList(roomInitMessage.data.room_id, 1, count);
                        if (listMessage?.data?.list != null && listMessage.data?.list.Length > 0)
                        {
                            var list = listMessage.data?.list;
                            if (list != null)
                            {
                                LiveRecordSourceList.AddRange(list);
                            }
                        }
                    }
                }
            }
            catch
            {
                // ignored
            }
            finally
            {
                IsLiveRecordBusy = false;
            }
        }

        private static async Task CopyLiveRecordDownloadUrlAsync(GridRecordContextMenuInfo info)
        {
            try
            {
                if (info?.Record is LiveRecordListViewModel liveRecord && !string.IsNullOrEmpty(liveRecord.Rid))
                {
                    using var client = new BililiveApiClient();
                    var message = await client.GetLiveRecordUrl(liveRecord.Rid);
                    var list = message?.data?.list;
                    if (list != null && list.Length > 0)
                    {
                        Utils.Utils.CopyToClipboard(string.Join(Environment.NewLine,
                                list.Where(x => !string.IsNullOrEmpty(x.url) || !string.IsNullOrEmpty(x.backup_url))
                                        .Select(x => string.IsNullOrEmpty(x.url) ? x.backup_url : x.url)
                        ));
                    }
                }
            }
            catch
            {
                // ignored
            }
        }

        private static IObservable<Unit> OpenLiveRecordUrl(GridRecordContextMenuInfo info)
        {
            return Observable.Start(() =>
            {
                try
                {
                    if (info?.Record is LiveRecordListViewModel liveRecord && !string.IsNullOrEmpty(liveRecord.Rid))
                    {
                        Utils.Utils.OpenUrl($@"https://live.bilibili.com/record/{liveRecord.Rid}");
                    }
                }
                catch
                {
                    // ignored
                }
            });
        }

        private IObservable<Unit> OpenDir(GridRecordContextMenuInfo info)
        {
            return Observable.Start(() =>
            {
                try
                {
                    if (info?.Record is LiveRecordListViewModel liveRecord && !string.IsNullOrEmpty(liveRecord.Rid))
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
                    // ignored
                }
            });
        }

        private IObservable<Unit> Download(GridRecordContextMenuInfo info)
        {
            return Observable.Start(() =>
            {
                try
                {
                    if (info?.Record is LiveRecordListViewModel liveRecord)
                    {
                        var root = Path.Combine(ConfigService.Config.MainDir, $@"{RoomId}", @"Replay");
                        DownloadTaskPool.DownloadAsync(liveRecord, root, ConfigService.Config.DownloadThreads).NoWarning(); //Async
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
            });
        }

        private void ShowWindow()
        {
            _window?.ShowWindow();
        }

        private void Exit()
        {
            DownloadTaskPool.StopAll();
            _window.CloseReason = CloseReason.ApplicationExitCall;
            _window.Close();
        }

        public void Dispose()
        {
            _diskMonitor?.Dispose();
            _roomIdMonitor?.Dispose();
            ConfigService?.Dispose();
        }
    }
}
