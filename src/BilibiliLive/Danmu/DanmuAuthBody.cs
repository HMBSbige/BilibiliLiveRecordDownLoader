using System.Text.Json.Serialization;

namespace BilibiliLive.Danmu;

// 字段顺序与网页端的认证包一致
internal sealed record DanmuAuthBody
(
	[property: JsonPropertyName("uid")] long UserId,
	[property: JsonPropertyName("roomid")] long RoomId,
	[property: JsonPropertyName("buvid"), JsonPropertyOrder(1)] string Buvid,
	[property: JsonPropertyName("key"), JsonPropertyOrder(1), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Key
)
{
	// 声明能解 brotli，服务器才会用它压缩下发的消息
	[JsonPropertyName("protover")]
	public ushort ProtocolVersion { get; } = DanmuPacket.BrotliVersion;

	[JsonPropertyName("platform")]
	public string Platform { get; } = "web";

	[JsonPropertyName("type")]
	public int Type { get; } = 2;
}
