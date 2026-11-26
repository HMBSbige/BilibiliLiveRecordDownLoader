using BilibiliLiveRecordDownLoader.Domain.Shared.Recording;

namespace BilibiliLiveRecordDownLoader.Domain.Shared.Localization;

public static class StreamQualityNames
{
	extension(StreamQuality quality)
	{
		// roomName 是接口给这个直播间的这一档起的名称，是中文：界面是其他语言时先用翻译过的已知名称
		public string GetDisplayName(string? roomName)
		{
			string? knownName = KnownName(quality.Qn);

			if (roomName is not null && (knownName is null || CultureInfo.CurrentUICulture.TwoLetterISOLanguageName is "zh"))
			{
				return roomName;
			}

			return knownName ?? quality.Qn.ToString(CultureInfo.InvariantCulture);
		}
	}

	// 同一 qn 在不同直播间可能叫法不同，注释里是见过的其他名称
	private static string? KnownName(long qn)
	{
		return qn switch
		{
			0 => AppLanguage.Current.QualityDefault,
			80 => AppLanguage.Current.QualitySmooth,
			150 => AppLanguage.Current.QualityHigh,
			// 720P 超清
			250 => AppLanguage.Current.QualitySuperHigh,
			// 1080P 蓝光
			400 => AppLanguage.Current.QualityBluRay,
			401 => AppLanguage.Current.QualityBluRayDolby,
			// 1080P 高码率、1080P 高码率 高帧率
			10000 => AppLanguage.Current.QualityOriginal,
			15000 => AppLanguage.Current.QualityQhd2K,
			20000 => AppLanguage.Current.QualityUltraHd4K,
			// 2K 原画、2K 原画 高帧率
			25000 => AppLanguage.Current.QualityOriginalTrueColor,
			30000 => AppLanguage.Current.QualityDolby,
			_ => null
		};
	}
}
