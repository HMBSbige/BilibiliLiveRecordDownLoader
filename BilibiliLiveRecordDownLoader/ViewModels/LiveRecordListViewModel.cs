using BilibiliLiveRecordDownLoader.BilibiliApi.Model;
using BilibiliLiveRecordDownLoader.Services;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using System;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace BilibiliLiveRecordDownLoader.ViewModels
{
    public class LiveRecordListViewModel : ReactiveObject
    {
        private readonly ILogger _logger;

        #region 字段

        private string _rid;
        private string _title;
        private string _cover;
        private string _areaName;
        private string _parentAreaName;
        private DateTime _startTime;
        private DateTime _endTime;
        private long _online;
        private long _danmuNum;
        private TimeSpan _length;
        private double _downloadProgress;

        #endregion

        #region 属性

        /// <summary>
        /// 视频id
        /// </summary>
        public string Rid
        {
            get => _rid;
            set => this.RaiseAndSetIfChanged(ref _rid, value);
        }

        /// <summary>
        /// 标题
        /// </summary>
        public string Title
        {
            get => _title;
            set => this.RaiseAndSetIfChanged(ref _title, value);
        }

        /// <summary>
        /// 封面地址
        /// </summary>
        public string Cover
        {
            get => _cover;
            set => this.RaiseAndSetIfChanged(ref _cover, value);
        }

        /// <summary>
        /// 分区名
        /// </summary>
        public string AreaName
        {
            get => _areaName;
            set => this.RaiseAndSetIfChanged(ref _areaName, value);
        }

        /// <summary>
        /// 主分区名
        /// </summary>
        public string ParentAreaName
        {
            get => _parentAreaName;
            set => this.RaiseAndSetIfChanged(ref _parentAreaName, value);
        }

        /// <summary>
        /// 开始时间
        /// </summary>
        public DateTime StartTime
        {
            get => _startTime;
            set => this.RaiseAndSetIfChanged(ref _startTime, value);
        }

        /// <summary>
        /// 结束时间
        /// </summary>
        public DateTime EndTime
        {
            get => _endTime;
            set => this.RaiseAndSetIfChanged(ref _endTime, value);
        }

        /// <summary>
        /// 人气峰值
        /// </summary>
        public long Online
        {
            get => _online;
            set => this.RaiseAndSetIfChanged(ref _online, value);
        }

        /// <summary>
        /// 弹幕数
        /// </summary>
        public long DanmuNum
        {
            get => _danmuNum;
            set => this.RaiseAndSetIfChanged(ref _danmuNum, value);
        }

        /// <summary>
        /// 视频长度
        /// </summary>
        public TimeSpan Length
        {
            get => _length;
            set => this.RaiseAndSetIfChanged(ref _length, value);
        }

        /// <summary>
        /// 下载进度
        /// </summary>
        public double DownloadProgress
        {
            get => _downloadProgress;
            set => this.RaiseAndSetIfChanged(ref _downloadProgress, value);
        }

        #endregion

        private LiveRecordDownloadTask LiveRecordDownloadTask { get; set; }
        private IDisposable _progressMonitor;

        public bool IsDownloading => LiveRecordDownloadTask?.IsDownloading ?? false;

        public LiveRecordListViewModel(ILogger logger, LiveRecordList data)
        {
            _logger = logger;
            CopyFrom(data);
            _downloadProgress = 0.0;
        }

        /// <summary>
        /// 开始或停止下载
        /// </summary>
        /// <returns></returns>
        public async Task StartOrStopAsync()
        {
            if (LiveRecordDownloadTask != null)
            {
                await LiveRecordDownloadTask.StartOrStopAsync();
            }
        }

        public void Attach(LiveRecordDownloadTask t)
        {
            _progressMonitor?.Dispose();
            LiveRecordDownloadTask = t;
            _progressMonitor = LiveRecordDownloadTask.ProgressUpdated
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(d => { DownloadProgress = d; },
                ex =>
                {
                    _logger.LogWarning(ex, @"下载回放出错");
                });
        }

        public void CopyFrom(LiveRecordList data)
        {
            Rid = data.rid;
            Title = data.title;
            Cover = data.cover;
            AreaName = data.area_name;
            ParentAreaName = data.parent_area_name;
            StartTime = ToDateTime(data.start_timestamp);
            EndTime = ToDateTime(data.end_timestamp);
            Online = data.online;
            DanmuNum = data.danmu_num;
            Length = ToTimeSpan(data.length);
        }

        private static DateTime ToDateTime(long timestamp)
        {
            try
            {
                return DateTimeOffset.FromUnixTimeSeconds(timestamp).ToLocalTime().DateTime;
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        private static TimeSpan ToTimeSpan(long length)
        {
            try
            {
                return TimeSpan.FromMilliseconds(length);
            }
            catch
            {
                return TimeSpan.Zero;
            }
        }
    }
}
