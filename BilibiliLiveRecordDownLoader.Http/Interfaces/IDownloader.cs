using BilibiliLiveRecordDownLoader.Shared.Interfaces;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BilibiliLiveRecordDownLoader.Http.Interfaces
{
	public interface IDownloader : IAsyncDisposable, IProgress
	{
		/// <summary>
		/// UA，默认应该是
		/// Mozilla/5.0 (Windows NT 6.1; Trident/7.0; rv:11.0) like Gecko
		/// </summary>
		string UserAgent { get; set; }

		/// <summary>
		/// 手动设置的 Cookie
		/// </summary>
		string? Cookie { get; set; }

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
