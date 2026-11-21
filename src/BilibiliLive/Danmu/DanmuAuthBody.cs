using System.Text.Json.Serialization;

namespace BilibiliLive.Danmu;

internal sealed record DanmuAuthBody(
	[property: JsonPropertyName("uid")] long UserId,
	[property: JsonPropertyName("roomid")] long RoomId,
	[property: JsonPropertyName("protover")] int ProtocolVersion,
	[property: JsonPropertyName("platform")] string Platform,
	[property: JsonPropertyName("type")] int Type,
	[property: JsonPropertyName("buvid")] string Buvid,
	[property: JsonPropertyName("key"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Key);
