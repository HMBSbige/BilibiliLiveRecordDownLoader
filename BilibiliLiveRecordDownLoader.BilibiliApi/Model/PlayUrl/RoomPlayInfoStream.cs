using System.Text.Json.Serialization;

namespace BilibiliApi.Model.PlayUrl;

public class RoomPlayInfoStream
{
	/// <summary>
	/// http_stream
	/// http_hls
	/// </summary>
	[JsonPropertyName(@"protocol_name")]
	public string? ProtocolName { get; set; }

	[JsonPropertyName(@"format")]
	public RoomPlayInfoStreamFormat[]? Format { get; set; }
}