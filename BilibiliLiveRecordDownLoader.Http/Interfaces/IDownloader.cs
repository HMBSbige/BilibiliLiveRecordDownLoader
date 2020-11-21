using BilibiliLiveRecordDownLoader.Shared.Interfaces;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BilibiliLiveRecordDownLoader.Http.Interfaces
{
	public interface IDownloader : IAsyncDisposable, IProgress
	{
		/// <summary>
		/// 下载目标
		/// </summary>
		Uri? Target { get; set; }

		/// <summary>
		/// 输出文件名，包括路径
		/// </summary>
		string? OutFileName { get; set; }

		/// <summary>
		/// 下载
		/// </summary>
		ValueTask DownloadAsync(CancellationToken token);
	}
}
