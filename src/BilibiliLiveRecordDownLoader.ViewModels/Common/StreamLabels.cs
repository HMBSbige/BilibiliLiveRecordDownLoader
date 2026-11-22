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
			StreamQuality.Smooth => AppLanguage.Current.QualitySmooth,
			StreamQuality.High => AppLanguage.Current.QualityHigh,
			StreamQuality.SuperHigh => AppLanguage.Current.QualitySuperHigh,
			StreamQuality.BluRay => AppLanguage.Current.QualityBluRay,
			StreamQuality.Original => AppLanguage.Current.QualityOriginal,
			StreamQuality.UltraHd4K => AppLanguage.Current.QualityUltraHd4K,
			StreamQuality.Dolby => AppLanguage.Current.QualityDolby,
			(StreamQuality)StreamQualityConsts.TrueOriginal => AppLanguage.Current.QualityTrueOriginal,
			_ => value.ToString()
		};
	}
}
