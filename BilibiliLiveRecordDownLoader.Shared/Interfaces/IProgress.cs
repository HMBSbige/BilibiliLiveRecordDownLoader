using System;

namespace BilibiliLiveRecordDownLoader.Shared.Interfaces
{
    public interface IProgress
    {
        /// <summary>
        /// 进度，[0.0,1.0]
        /// </summary>
        double Progress { get; }

        /// <summary>
        /// 当前下载速度，单位字节
        /// </summary>
        IObservable<double> CurrentSpeed { get; }

        /// <summary>
        /// 当前状态
        /// </summary>
        IObservable<string> Status { get; }
    }
}
