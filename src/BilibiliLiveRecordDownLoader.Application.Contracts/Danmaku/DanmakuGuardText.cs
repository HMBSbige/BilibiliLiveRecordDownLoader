using BilibiliLiveRecordDownLoader.Domain.Shared.Localization;
using System.Globalization;

namespace BilibiliLiveRecordDownLoader.Application.Contracts.Danmaku;

public static class DanmakuGuardText
{
	public static string Format(GuardLevel level, int months, bool isRenewal)
	{
		string name = FormatLevel(level);
		return (months > 1, isRenewal) switch
		{
			(true, true) => AppLanguage.Current.DanmakuGuardRenewalMonthsText.ToString(name, months.ToString(CultureInfo.CurrentCulture)),
			(true, false) => AppLanguage.Current.DanmakuGuardMonthsText.ToString(name, months.ToString(CultureInfo.CurrentCulture)),
			(false, true) => AppLanguage.Current.DanmakuGuardRenewalText.ToString(name),
			(false, false) => AppLanguage.Current.DanmakuGuardText.ToString(name)
		};
	}

	// 等级可能是没定义的值，按最低的舰长显示
	public static string FormatLevel(GuardLevel level)
	{
		return level switch
		{
			GuardLevel.Governor => AppLanguage.Current.GuardLevelGovernor,
			GuardLevel.Admiral => AppLanguage.Current.GuardLevelAdmiral,
			_ => AppLanguage.Current.GuardLevelCaptain
		};
	}
}
