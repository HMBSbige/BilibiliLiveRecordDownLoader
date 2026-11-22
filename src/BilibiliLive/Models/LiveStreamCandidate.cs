namespace BilibiliLive.Models;

public closed record LiveStreamCandidate(StreamProtocol Protocol, StreamFormat Format, StreamCodec Codec, long Qn, IReadOnlyList<Uri> Urls)
{
	/// <summary>A stream as getRoomPlayInfo lists it.</summary>
	/// <param name="AcceptQualities">The qualities the API offers for this codec, which may differ between codecs of the same room.</param>
	public sealed record Listed(StreamProtocol Protocol, StreamFormat Format, StreamCodec Codec, long Qn, IReadOnlyList<long> AcceptQualities, IReadOnlyList<Uri> Urls) : LiveStreamCandidate(Protocol, Format, Codec, Qn, Urls);

	/// <summary>An fMP4 variant of the HLS master playlist from play-gateway, with the URL of every pathway that serves it.</summary>
	/// <remarks>The master playlist lists every quality, including some that getRoomPlayInfo holds back from the requester.</remarks>
	public sealed record Variant(StreamCodec Codec, long Qn, IReadOnlyList<Uri> Urls) : LiveStreamCandidate(StreamProtocol.HttpHls, StreamFormat.Fmp4, Codec, Qn, Urls);
}
