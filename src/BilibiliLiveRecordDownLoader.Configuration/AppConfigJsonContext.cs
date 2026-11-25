namespace BilibiliLiveRecordDownLoader.Configuration;

[JsonSerializable(typeof(AppConfig))]
[JsonSerializable(typeof(RetiredSettings))]
[JsonSourceGenerationOptions
(
	PropertyNameCaseInsensitive = true,
	ReadCommentHandling = JsonCommentHandling.Skip,
	AllowTrailingCommas = true,
	UseStringEnumConverter = true,
	DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
	Converters = [typeof(StreamQualityJsonConverter)]
)]
internal sealed partial class AppConfigJsonContext : JsonSerializerContext;
