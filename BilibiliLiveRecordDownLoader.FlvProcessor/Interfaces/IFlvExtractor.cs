using BilibiliLiveRecordDownLoader.Shared.Interfaces;

namespace BilibiliLiveRecordDownLoader.FlvProcessor.Interfaces;

public interface IFlvExtractor : IAsyncDisposable, IProgress
{
	/// <summary>
	/// 默认 4096
	/// </summary>
	int BufferSize { get; set; }

	/// <summary>
	/// 输出时是否使用异步
	/// </summary>
	bool IsAsync { get; set; }

	/// <summary>
	/// 输出目录
	/// </summary>
	string? OutputDir { get; set; }

	/// <summary>
	/// 输出的视频路径
	/// </summary>
	string? OutputVideo { get; }

	/// <summary>
	/// 输出的音频路径
	/// </summary>
	string? OutputAudio { get; }

	/// <summary>
	/// 提取视频和音频
	/// </summary>
	/// <param name="path">待提取的 FLV 文件</param>
	/// <param name="token"></param>
	/// <returns></returns>
	ValueTask ExtractAsync(string path, CancellationToken token);
}
