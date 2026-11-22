namespace BilibiliLive.Streams;

/// <summary>Learns which bytes of a copied HLS stream make up each segment, so a recorder can switch files between segments without parsing the stream.</summary>
public interface IHlsSegmentObserver
{
	/// <summary>Called right before the init segment of an fMP4 stream is written to the destination.</summary>
	void OnInitSegment(ReadOnlySpan<byte> data);

	/// <summary>Called right before a media segment is written to the destination, which then receives exactly <paramref name="data"/>.</summary>
	void OnMediaSegment(ReadOnlySpan<byte> data);
}
