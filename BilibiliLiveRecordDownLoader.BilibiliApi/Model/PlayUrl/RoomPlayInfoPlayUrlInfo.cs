using System.Text.Json.Serialization;

namespace BilibiliApi.Model.PlayUrl;

public class RoomPlayInfoPlayUrlInfo
{
	[JsonPropertyName(@"playurl")]
	public RoomPlayInfoPlayUrl? PlayUrl { get; set; }

	public class RoomPlayInfoPlayUrl
	{
		[JsonPropertyName(@"stream")]
		public RoomPlayInfoStream[]? StreamInfo { get; set; }
	}
}
