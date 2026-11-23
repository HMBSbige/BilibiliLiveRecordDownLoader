namespace BilibiliLive.Streams;

internal sealed class HlsSegmentTracker(int historyCapacity)
{
	// 按播放列表条目而不是解析后的地址记录，因为刷新可能被重定向到另一个 CDN 节点
	private readonly SegmentHistory _seen = new(historyCapacity);

	// 只在正在复制的时代内有意义，重启进入新时代后编号可能从头开始
	private long? _last;

	// 不带序号的刷新入队的分片推不动游标，它们按列表顺序紧接在游标之后，只能记下个数
	private long _unnumbered;

	public bool HasSeen(HlsSegment segment)
	{
		return _seen.Contains(segment.Uri);
	}

	// 刷新可能来自窗口落后的 CDN 节点，第一次列出的条目也可能比已经入队的更早
	public bool IsAhead(HlsSegment segment)
	{
		return (segment.Sequence, _last) is not ({ } sequence, { } last) || sequence > last;
	}

	// 更早的分片不推进游标，其他时代的编号也未必和游标同一套
	public void Skip(HlsSegment segment)
	{
		_seen.Add(segment.Uri);
	}

	public void Add(HlsSegment segment)
	{
		_seen.Add(segment.Uri);

		if (segment.Sequence is null)
		{
			++_unnumbered;
		}
		else
		{
			Advance(segment);
		}
	}

	// 游标前进几个编号，就有几个不带序号入队的分片落到游标之内
	public void Advance(HlsSegment segment)
	{
		if (segment.Sequence is { } sequence && IsAhead(segment))
		{
			_unnumbered = _last is { } last ? Math.Max(0, _unnumbered - (sequence - last)) : 0;
			_last = sequence;
		}
	}

	public long CountMissed(HlsPlaylist playlist)
	{
		return playlist.Segments is [{ Sequence: { } first }, ..] && _last is { } last ? Math.Max(0, first - last - 1 - _unnumbered) : 0;
	}
}
