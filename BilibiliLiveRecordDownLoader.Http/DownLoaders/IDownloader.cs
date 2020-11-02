using System;
using System.Threading;
using System.Threading.Tasks;

namespace BilibiliLiveRecordDownLoader.Http.DownLoaders
{
    public interface IDownloader : IAsyncDisposable
    {
        /// <summary>
        /// 进度，[0.0,1.0]
        /// </summary>
        public IObservable<double> ProgressUpdated { get; }

        /// <summary>
        /// 当前下载速度，单位字节
        /// </summary>
        public IObservable<double> CurrentSpeed { get; }

        /// <summary>
        /// UA，默认应该是
        /// Mozilla/5.0 (Windows NT 6.1; Trident/7.0; rv:11.0) like Gecko
        /// </summary>
        public string UserAgent { get; set; }

        /// <summary>
        /// 手动设置的 Cookie
        /// </summary>
        public string Cookie { get; set; }

        /// <summary>
        /// 下载目标
        /// </summary>
        public Uri Target { get; set; }

        /// <summary>
        /// 线程数
        /// </summary>
        public ushort Threads { get; set; }

        /// <summary>
        /// 临时文件夹
        /// </summary>
        public string TempDir { get; set; }

        /// <summary>
        /// 输出文件名，包括路径
        /// </summary>
        public string OutFileName { get; set; }

        /// <summary>
        /// 下载
        /// </summary>
        public ValueTask DownloadAsync(CancellationToken token);
    }
}
