namespace BilibiliLive.Streams;

/// <summary>得知复制出的 HLS 流里哪些字节组成各个分片，录制端不解析流也能在分片之间切换文件。</summary>
public interface IHlsSegmentObserver
{
	/// <summary>在 fMP4 流的初始化段写入目标流之前调用。</summary>
	void OnInitSegment(ReadOnlySpan<byte> data);

	/// <summary>在媒体分片写入目标流之前调用，目标流接下来收到的正好是 <paramref name="data"/>。</summary>
	void OnMediaSegment(ReadOnlySpan<byte> data);
}
