namespace BilibiliLive.Models;

public closed record LiveStreamCandidate(StreamProtocol Protocol, StreamFormat Format, StreamCodec Codec, long Qn, IReadOnlyList<Uri> Urls)
{
	/// <param name="AcceptQualities">接口为这种编码提供的画质，同一房间的不同编码之间可能不同</param>
	public sealed record Listed(StreamProtocol Protocol, StreamFormat Format, StreamCodec Codec, long Qn, IReadOnlyList<long> AcceptQualities, IReadOnlyList<Uri> Urls) : LiveStreamCandidate(Protocol, Format, Codec, Qn, Urls);

	/// <remarks>主播放列表列出全部画质，包括 getRoomPlayInfo 不给请求者的一些画质</remarks>
	public sealed record Variant(StreamCodec Codec, long Qn, IReadOnlyList<Uri> Urls) : LiveStreamCandidate(StreamProtocol.HttpHls, StreamFormat.Fmp4, Codec, Qn, Urls);
}
