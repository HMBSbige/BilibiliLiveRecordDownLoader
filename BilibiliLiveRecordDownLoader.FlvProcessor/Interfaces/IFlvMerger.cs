using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BilibiliLiveRecordDownLoader.FlvProcessor.Interfaces
{
    public interface IFlvMerger
    {
        /// <summary>
        /// 进度，[0.0,1.0]
        /// </summary>
        public IObservable<double> ProgressUpdated { get; }

        /// <summary>
        /// 当前下载速度，单位字节
        /// </summary>
        public IObservable<double> CurrentSpeed { get; }

        public int BufferSize { get; set; }

        /// <summary>
        /// 输出 FLV 时是否使用异步
        /// </summary>
        public bool IsAsync { get; set; }

        /// <summary>
        /// 需要合并的 FLV
        /// </summary>
        public IEnumerable<string> Files { get; }

        /// <summary>
        /// 添加 FLV 文件
        /// </summary>
        public void Add(string path);

        /// <summary>
        /// 添加多个 FLV 文件
        /// </summary>
        public void AddRange(IEnumerable<string> path);

        /// <summary>
        /// 合并 FLV 到指定路径
        /// </summary>
        /// <param name="path">输出的 FLV 路径</param>
        /// <param name="token"></param>
        ValueTask MergeAsync(string path, CancellationToken token);
    }
}
