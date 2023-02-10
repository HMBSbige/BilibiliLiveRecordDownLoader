using BilibiliLiveRecordDownLoader.FlvProcessor.Enums;
using BilibiliLiveRecordDownLoader.FlvProcessor.Models;

namespace BilibiliLiveRecordDownLoader.FlvProcessor.Interfaces;

internal interface IVideoWriter
{
	int BufferSize { get; init; }
	/// <summary>
	/// 输出时是否使用异步
	/// </summary>
	bool IsAsync { get; init; }
	string Path { get; }
	void Write(Memory<byte> buffer, uint timestamp, FrameType type);
	ValueTask FinishAsync(FractionUInt32 averageFrameRate);
}
