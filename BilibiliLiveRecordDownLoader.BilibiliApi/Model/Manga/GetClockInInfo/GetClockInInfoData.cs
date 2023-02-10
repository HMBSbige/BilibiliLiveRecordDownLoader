namespace BilibiliApi.Model.Manga.GetClockInInfo;

public class GetClockInInfoData
{
	/// <summary>
	/// 连续签到天数
	/// </summary>
	public long day_count { get; set; }

	/// <summary>
	/// 1 表示已签到
	/// 0 表示未签到
	/// </summary>
	public long status { get; set; }
}
