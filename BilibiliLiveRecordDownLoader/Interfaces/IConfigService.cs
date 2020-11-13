using BilibiliLiveRecordDownLoader.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BilibiliLiveRecordDownLoader.Interfaces
{
    public interface IConfigService : IService, IDisposable
    {
        /// <summary>
        /// 配置文件
        /// </summary>
        Config Config { get; }

        /// <summary>
        /// 配置文件路径
        /// </summary>
        string FilePath { get; set; }

        /// <summary>
        /// 保存配置
        /// </summary>
        Task SaveAsync(CancellationToken token);

        /// <summary>
        /// 加载配置
        /// </summary>
        Task LoadAsync(CancellationToken token);
    }
}
