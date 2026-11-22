namespace BilibiliLive.Models;

public closed record LiveStreamCandidate(StreamProtocol Protocol, StreamFormat Format, long Qn, IReadOnlyList<Uri> Urls)
{
	/// <summary>A stream as getRoomPlayInfo lists it.</summary>
	/// <param name="AcceptQualities">The qualities the API offers for this codec, which may differ between codecs of the same room.</param>
	public sealed record Listed(StreamProtocol Protocol, StreamFormat Format, StreamCodec Codec, long Qn, IReadOnlyList<long> AcceptQualities, IReadOnlyList<Uri> Urls) : LiveStreamCandidate(Protocol, Format, Qn, Urls);

	/// <summary>A listed stream with the transcoding suffix taken off its name, which may lead to the untranscoded original.</summary>
	/// <remarks>Has no codec, because a transcoded AVC stream can come from an HEVC original; only the init segment tells which one it is.</remarks>
	public sealed record Source(StreamProtocol Protocol, StreamFormat Format, IReadOnlyList<Uri> Urls) : LiveStreamCandidate(Protocol, Format, OriginalQuality, Urls);

	public const long OriginalQuality = 10000;
}
