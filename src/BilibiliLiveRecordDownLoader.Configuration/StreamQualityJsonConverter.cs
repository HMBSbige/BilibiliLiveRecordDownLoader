using BilibiliLiveRecordDownLoader.Domain.Shared.Recording;

namespace BilibiliLiveRecordDownLoader.Configuration;

// 写成 qn；4.0 把画质存成枚举名，也要能读
internal sealed class StreamQualityJsonConverter : JsonConverter<StreamQuality>
{
	private static readonly Dictionary<string, StreamQuality> LegacyNames = new(StringComparer.OrdinalIgnoreCase)
	{
		["Smooth"] = StreamQuality.Smooth,
		["High"] = StreamQuality.High,
		["SuperHigh"] = StreamQuality.SuperHigh,
		["BluRay"] = StreamQuality.BluRay,
		["Original"] = StreamQuality.Original,
		["UltraHd4K"] = StreamQuality.UltraHd4K,
		["Dolby"] = StreamQuality.Dolby
	};

	public override StreamQuality Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType is JsonTokenType.Number && reader.TryGetInt64(out long qn))
		{
			return new StreamQuality(qn);
		}

		if (reader.TokenType is JsonTokenType.String && reader.GetString() is { } text)
		{
			if (LegacyNames.TryGetValue(text, out StreamQuality quality))
			{
				return quality;
			}

			if (long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out qn))
			{
				return new StreamQuality(qn);
			}
		}

		throw new JsonException("The stream quality is neither a qn nor a known name.");
	}

	public override void Write(Utf8JsonWriter writer, StreamQuality value, JsonSerializerOptions options)
	{
		writer.WriteNumberValue(value.Qn);
	}
}
