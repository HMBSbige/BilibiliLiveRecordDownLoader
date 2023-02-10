using BilibiliLiveRecordDownLoader.Shared.Interfaces;

namespace BilibiliLiveRecordDownLoader.FlvProcessor.Interfaces;

public interface IFlvMerger : IAsyncDisposable, IProgress
{
	int BufferSize { get; init; }

	/// <summary>
	/// 输出 FLV 时是否使用异步
	/// </summary>
	bool IsAsync { get; init; }

	/// <summary>
	/// 需要合并的 FLV
	/// </summary>
	IEnumerable<string> Files { get; }

	/// <summary>
	/// 添加 FLV 文件
	/// </summary>
	void Add(string path);

	/// <summary>
	/// 添加多个 FLV 文件
	/// </summary>
	void AddRange(IEnumerable<string> path);

	/// <summary>
	/// 合并 FLV 到指定路径
	/// </summary>
	/// <param name="path">输出的 FLV 路径</param>
	/// <param name="token"></param>
	ValueTask MergeAsync(string path, CancellationToken token);
}
