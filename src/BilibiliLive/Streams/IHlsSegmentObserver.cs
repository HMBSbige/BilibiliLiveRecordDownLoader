namespace BilibiliLive.Streams;

public interface IHlsSegmentObserver
{
	/// <summary>在 fMP4 流的初始化段写入目标流之前调用</summary>
	void OnInitSegment(ReadOnlySpan<byte> data);

	/// <summary>在媒体分片写入目标流之前调用，目标流接下来收到的正好是 <paramref name="data"/></summary>
	void OnMediaSegment(ReadOnlySpan<byte> data);
}
