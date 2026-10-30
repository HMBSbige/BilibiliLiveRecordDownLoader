namespace BilibiliLiveRecordDownLoader.Configuration;

[JsonSerializable(typeof(AppConfig))]
[JsonSerializable(typeof(JsonObject))]
[JsonSourceGenerationOptions
(
	WriteIndented = true,
	IndentCharacter = '\t',
	IndentSize = 1,
	PropertyNameCaseInsensitive = true,
	ReadCommentHandling = JsonCommentHandling.Skip,
	AllowTrailingCommas = true,
	UseStringEnumConverter = true,
	DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
internal partial class AppConfigJsonContext : JsonSerializerContext;
