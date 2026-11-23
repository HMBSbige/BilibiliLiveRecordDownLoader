namespace BilibiliLive.Streams;

/// <param name="Sequence">播放列表不给分片编号时为 <see langword="null"/></param>
internal sealed record HlsSegment(string Uri, string? MapUri, long DiscontinuitySequence, long? Sequence)
{
	// 只有初始化段和时间线都相同的分片才能写进同一个文件
	public bool IsSameEra(HlsSegment other)
	{
		return MapUri == other.MapUri && DiscontinuitySequence == other.DiscontinuitySequence;
	}
}
