namespace BilibiliLiveRecordDownLoader.ViewModels.Common;

public static class StreamLabels
{
	public static string Of(Enum value)
	{
		return value switch
		{
			StreamCodec.Avc => "AVC (H.264)",
			StreamCodec.Hevc => "HEVC (H.265)",
			StreamCodec.Av1 => "AV1",
			StreamFormat.Fmp4 => "fMP4",
			StreamFormat.Ts => "MPEG-TS",
			StreamFormat.Flv => "FLV",
			_ => value.ToString()
		};
	}
}
