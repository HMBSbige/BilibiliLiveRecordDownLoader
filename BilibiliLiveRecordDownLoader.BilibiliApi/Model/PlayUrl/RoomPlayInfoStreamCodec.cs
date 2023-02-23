using System.Text.Json.Serialization;

namespace BilibiliApi.Model.PlayUrl;

public class RoomPlayInfoStreamCodec
{
	/// <summary>
	/// avc
	/// hevc
	/// </summary>
	[JsonPropertyName(@"codec_name")]
	public string? CodecName { get; set; }

	[JsonPropertyName(@"current_qn")]
	public long CurrentQn { get; set; }

	/// <summary>
	/// _bluray 伪原画
	/// </summary>
	[JsonPropertyName(@"base_url")]
	public string? BaseUrl { get; set; }

	[JsonPropertyName(@"url_info")]
	public RoomPlayInfoStreamUrlInfo[]? UrlInfo { get; set; }
}
