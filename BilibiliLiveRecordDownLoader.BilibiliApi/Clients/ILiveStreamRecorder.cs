using BilibiliLiveRecordDownLoader.Shared.Interfaces;

namespace BilibiliApi.Clients;

public interface ILiveStreamRecorder : IProgress, IHttpClient, IAsyncDisposable
{
	/// <summary>
	/// 直播流下载完后，输出文件的 Task
	/// </summary>
	Task WriteToFileTask { get; }

	/// <summary>
	/// 初始化
	/// </summary>
	/// <param name="source">下载源</param>
	/// <param name="cancellationToken"></param>
	ValueTask InitializeAsync(Uri[] source, CancellationToken cancellationToken = default);

	/// <summary>
	/// 开始下载
	/// </summary>
	/// <param name="outFilePath">输出文件路径，允许不包括扩展名</param>
	/// <param name="cancellationToken"></param>
	ValueTask DownloadAsync(string outFilePath, CancellationToken cancellationToken = default);
}
