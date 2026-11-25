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

	public static string Of(StreamQuality quality)
	{
		return quality.KnownName ?? quality.Qn.ToString(CultureInfo.InvariantCulture);
	}

	// 接口给的名称是中文，界面是其他语言时先用翻译过的已知名称
	public static string OfQuality(long qn, IReadOnlyDictionary<long, string> roomNames)
	{
		StreamQuality quality = new(qn);

		return roomNames.GetValueOrDefault(qn) is { } roomName && (quality.KnownName is null || CultureInfo.CurrentUICulture.TwoLetterISOLanguageName is "zh") ? roomName : Of(quality);
	}
}
