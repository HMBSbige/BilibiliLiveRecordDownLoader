using System;
using System.Reactive;
using System.Reactive.Linq;
using BilibiliLiveRecordDownLoader.BilibiliApi;
using Microsoft.WindowsAPICodePack.Dialogs;
using ReactiveUI;

namespace BilibiliLiveRecordDownLoader.ViewModels
{
    public class MainWindowViewModel : ReactiveObject, IDisposable
    {
        #region 字段

        private long _roomId;
        private string _imageUri;
        private string _name;
        private long _uid;
        private long _level;
        private string _mainDir;
        private string _diskUsageProgressBarText;
        private double _diskUsageProgressBarValue;

        #endregion

        #region 属性

        public long RoomId
        {
            get => _roomId;
            set => this.RaiseAndSetIfChanged(ref _roomId, value);
        }

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

        public string MainDir
        {
            get => _mainDir;
            set => this.RaiseAndSetIfChanged(ref _mainDir, value);
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

        #endregion

        #region Monitor

        private readonly IDisposable _diskMonitor;
        private readonly IDisposable _roomIdMonitor;

        #endregion

        #region Command

        public ReactiveCommand<Unit, Unit> SelectMainDirCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenMainDirCommand { get; }

        #endregion

        public MainWindowViewModel()
        {
            RoomId = 732;

            _roomIdMonitor = this.WhenAnyValue(x => x.RoomId)
                    .Throttle(TimeSpan.FromMilliseconds(1000))
                    .DistinctUntilChanged()
                    .Where(i => i > 0)
                    .ObserveOn(RxApp.MainThreadScheduler)
                    .Subscribe(GetAnchorInfo);

            _diskMonitor = Observable.Interval(TimeSpan.FromSeconds(1))
                    .ObserveOn(RxApp.MainThreadScheduler)
                    .Subscribe(GetDiskUsage);

            SelectMainDirCommand = ReactiveCommand.Create(SelectDirectory);
            OpenMainDirCommand = ReactiveCommand.Create(OpenDirectory);
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
                InitialDirectory = MainDir
            };
            if (dlg.ShowDialog() == CommonFileDialogResult.Ok)
            {
                MainDir = dlg.FileName;
            }
        }

        private void OpenDirectory()
        {
            Utils.Utils.OpenDir(MainDir);
        }

        private void GetDiskUsage(long _)
        {
            var (availableFreeSpace, totalSize) = Utils.Utils.GetDiskUsage(MainDir);
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

        private async void GetAnchorInfo(long roomId)
        {
            try
            {
                using var client = new BililiveApiClient();
                var msg = await client.GetAnchorInfo(roomId);

                if (msg.code != 0 || msg.data?.info == null) return;

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

        public void Dispose()
        {
            _diskMonitor?.Dispose();
            _roomIdMonitor?.Dispose();
        }
    }
}
