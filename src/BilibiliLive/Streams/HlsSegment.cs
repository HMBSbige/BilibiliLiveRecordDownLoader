namespace BilibiliLive.Streams;

internal sealed record HlsSegment(string Uri, string? MapUri, long DiscontinuitySequence, long? Sequence)
{
	public bool IsSameEra(HlsSegment other)
	{
		return MapUri == other.MapUri && DiscontinuitySequence == other.DiscontinuitySequence;
	}
}
