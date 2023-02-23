using BilibiliLiveRecordDownLoader.Shared.Interfaces;

namespace BilibiliApi.Clients;

public interface ILiveStreamRecorder : IProgress, IHttpClient, IAsyncDisposable
{
	/// <summary>
	/// 下载源
	/// </summary>
	Uri? Source { get; set; }

	/// <summary>
	/// 输出文件路径，允许不包括扩展名
	/// </summary>
	string? OutFilePath { get; set; }

	/// <summary>
	/// 直播流下载完后，输出文件的 Task
	/// </summary>
	Task WriteToFileTask { get; }

	/// <summary>
	/// 初始化
	/// </summary>
	ValueTask InitAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// 下载
	/// </summary>
	ValueTask DownloadAsync(CancellationToken cancellationToken = default);
}
