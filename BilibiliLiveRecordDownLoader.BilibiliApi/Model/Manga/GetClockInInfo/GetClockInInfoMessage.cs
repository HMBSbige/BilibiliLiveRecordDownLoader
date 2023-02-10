namespace BilibiliApi.Model.Manga.GetClockInInfo;

public class GetClockInInfoMessage
{
	/// <summary>
	/// 正常为 0
	/// </summary>
	public long code { get; set; }

	/// <summary>
	/// 消息
	/// </summary>
	public string? msg { get; set; }

	/// <summary>
	/// 漫画签到信息
	/// </summary>
	public GetClockInInfoData? data { get; set; }
}
