namespace BilibiliApi.Model.PlayUrl;

public class PlayUrlData
{
	/// <summary>
	/// 当前 qn 参数
	/// </summary>
	public long current_qn { get; set; }

	/// <summary>
	/// qn 参数与描述
	/// </summary>
	public Quality_Description[]? quality_description { get; set; }

	/// <summary>
	/// 直播地址信息
	/// </summary>
	public Durl[]? durl { get; set; }
}
