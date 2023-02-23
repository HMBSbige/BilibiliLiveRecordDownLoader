using BilibiliApi.Enums;
using System.Text.Json.Serialization;

namespace BilibiliApi.Model.PlayUrl;

public class RoomPlayInfoData
{
	[JsonPropertyName(@"live_status")]
	public LiveStatus LiveStatus { get; set; }

	[JsonPropertyName(@"playurl_info")]
	public RoomPlayInfoPlayUrlInfo? PlayUrlInfo { get; set; }
}