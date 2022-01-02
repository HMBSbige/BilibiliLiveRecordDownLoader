namespace BilibiliApi.Model.FansMedal;

public class LiveFansMedalData
{
	/// <summary>
	/// 粉丝徽章列表
	/// </summary>
	public FansMedalList[]? items { get; set; }

	/// <summary>
	/// 分页信息
	/// </summary>
	public Pageinfo? page_info { get; set; }

	/// <summary>
	/// 徽章数
	/// </summary>
	public long count { get; set; }
}
