using System.Text.Json.Serialization;

namespace BilibiliApi.Model.PlayUrl;

public class RoomPlayInfoStreamFormat
{
	/// <summary>
	/// flv
	/// ts
	/// fmp4
	/// </summary>
	[JsonPropertyName(@"format_name")]
	public string? FormatName { get; set; }

	[JsonPropertyName(@"codec")]
	public RoomPlayInfoStreamCodec[]? Codec { get; set; }
}
