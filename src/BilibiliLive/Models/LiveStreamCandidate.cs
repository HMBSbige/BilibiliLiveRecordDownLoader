namespace BilibiliLive.Models;

public closed record LiveStreamCandidate(StreamProtocol Protocol, StreamFormat Format, StreamCodec Codec, long Qn, IReadOnlyList<Uri> Urls)
{
	/// <summary>地址路径里的流名，各个主机相同；路径不是哔哩哔哩的已知形式时为 null</summary>
	public string? StreamName { get; init; }

	/// <summary><paramref name="other"/> 是不是哔哩哔哩从这路流转码出来的：转码的流名是源流名加上 "_" 和后缀</summary>
	public bool IsSourceOf(LiveStreamCandidate other)
	{
		return StreamName is { } name && other.StreamName is { } transcode && transcode.StartsWith(name, StringComparison.Ordinal) && transcode.AsSpan(name.Length) is ['_', _, ..];
	}

	/// <param name="AcceptQualities">接口为这种编码提供的画质，同一房间的不同编码之间可能不同</param>
	public sealed record Listed(StreamProtocol Protocol, StreamFormat Format, StreamCodec Codec, long Qn, IReadOnlyList<long> AcceptQualities, IReadOnlyList<Uri> Urls) : LiveStreamCandidate(Protocol, Format, Codec, Qn, Urls);

	/// <param name="QualityName">主播放列表给这个画质起的名称</param>
	/// <remarks>主播放列表列出全部画质，包括 getRoomPlayInfo 不给请求者的一些画质</remarks>
	public sealed record Variant(StreamCodec Codec, long Qn, string? QualityName, IReadOnlyList<Uri> Urls) : LiveStreamCandidate(StreamProtocol.HttpHls, StreamFormat.Fmp4, Codec, Qn, Urls);
}
