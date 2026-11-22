namespace BilibiliLive.Streams;

internal sealed class HlsSegmentTracker(int historyCapacity)
{
	// Keyed by the playlist entry rather than the resolved URI, because refreshes may redirect to another CDN node
	private readonly SegmentHistory _seen = new(historyCapacity);

	// Only meaningful within the era being copied, because a restart into a new era may number its segments from the start again
	private long? _last;

	public bool HasSeen(HlsSegment segment)
	{
		return _seen.Contains(segment.Uri);
	}

	// A refresh may come from a CDN node whose window lags behind, where an entry listed for the first time can still be older than what was queued
	public bool IsAhead(HlsSegment segment)
	{
		return (segment.Sequence, _last) is not ({ } sequence, { } last) || sequence > last;
	}

	// Older eras need not share the numbering of the cursor
	public void Skip(HlsSegment segment)
	{
		_seen.Add(segment.Uri);
	}

	public void Add(HlsSegment segment)
	{
		_seen.Add(segment.Uri);

		if (segment.Sequence is { } sequence)
		{
			_last = sequence;
		}
	}

	public long CountMissed(HlsPlaylist playlist)
	{
		return playlist.Segments is [{ Sequence: { } first }, ..] && _last is { } last && first > last + 1 ? first - last - 1 : 0;
	}
}
