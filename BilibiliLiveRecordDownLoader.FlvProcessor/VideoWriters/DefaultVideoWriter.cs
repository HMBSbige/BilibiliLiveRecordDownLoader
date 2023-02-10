using BilibiliLiveRecordDownLoader.FlvProcessor.Enums;
using BilibiliLiveRecordDownLoader.FlvProcessor.Interfaces;
using BilibiliLiveRecordDownLoader.FlvProcessor.Models;

namespace BilibiliLiveRecordDownLoader.FlvProcessor.VideoWriters;

internal class DefaultVideoWriter : IVideoWriter
{
	public int BufferSize { get; init; }
	public bool IsAsync { get; init; }
	public string Path => string.Empty;

	public void Write(Memory<byte> buffer, uint timestamp, FrameType type) { }
	public ValueTask FinishAsync(FractionUInt32 averageFrameRate) { return default; }
}
