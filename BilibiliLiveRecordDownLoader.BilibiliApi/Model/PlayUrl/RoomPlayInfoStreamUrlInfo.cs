using System.Text.Json.Serialization;

namespace BilibiliApi.Model.PlayUrl;

public class RoomPlayInfoStreamUrlInfo
{
	[JsonPropertyName(@"host")]
	public string? Host { get; set; }

	[JsonPropertyName(@"extra")]
	public string? Extra { get; set; }
}
