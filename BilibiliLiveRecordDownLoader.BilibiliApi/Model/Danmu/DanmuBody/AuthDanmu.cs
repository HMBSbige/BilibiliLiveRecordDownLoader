using System.Text.Json.Serialization;

namespace BilibiliApi.Model.Danmu.DanmuBody;

public record AuthDanmu
{
	[JsonPropertyName(@"roomid")]
	public long RoomId { get; set; }

	[JsonPropertyName(@"uid")]
	public long UserId { get; set; }

	[JsonPropertyName(@"protover")]
	public short ProtocolVersion { get; set; }

	[JsonPropertyName(@"key")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public string? Token { get; set; }
}

[JsonSerializable(typeof(AuthDanmu), GenerationMode = JsonSourceGenerationMode.Serialization)]
[JsonSourceGenerationOptions(
	IgnoreReadOnlyProperties = true,
	WriteIndented = false)]
public partial class AuthDanmuJsonSerializerContext : JsonSerializerContext;
