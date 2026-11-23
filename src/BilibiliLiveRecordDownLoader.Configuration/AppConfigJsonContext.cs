namespace BilibiliLiveRecordDownLoader.Configuration;

[JsonSerializable(typeof(AppConfig))]
[JsonSourceGenerationOptions
(
	PropertyNameCaseInsensitive = true,
	ReadCommentHandling = JsonCommentHandling.Skip,
	AllowTrailingCommas = true,
	UseStringEnumConverter = true,
	DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
internal sealed partial class AppConfigJsonContext : JsonSerializerContext;
