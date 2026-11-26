namespace BilibiliLiveRecordDownLoader.ViewModels.Common;

public static class StreamLabels
{
	public static string Of(Enum value)
	{
		return value switch
		{
			StreamCodec codec => Of(codec),
			StreamFormat format => Of(format),
			_ => value.ToString()
		};
	}

	public static string Of(StreamCodec codec)
	{
		return codec switch
		{
			StreamCodec.Avc => "AVC (H.264)",
			StreamCodec.Hevc => "HEVC (H.265)",
			StreamCodec.Av1 => "AV1"
		};
	}

	public static string Of(StreamFormat format)
	{
		return format switch
		{
			StreamFormat.Fmp4 => "fMP4",
			StreamFormat.Ts => "MPEG-TS",
			StreamFormat.Flv => "FLV"
		};
	}

	public static string Of(StreamQuality quality, IReadOnlyDictionary<long, string>? roomNames = null)
	{
		return quality.GetDisplayName(roomNames?.GetValueOrDefault(quality.Qn));
	}
}
