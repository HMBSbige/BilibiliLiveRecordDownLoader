namespace BilibiliApi.Model.PlayUrl;

public class PlayUrlMessage
{
	/// <summary>
	/// 正常返回 0
	/// </summary>
	public long code { get; set; }

	/// <summary>
	/// 正常返回 "0"，否则返回错误信息
	/// </summary>
	public string? message { get; set; }

	/// <summary>
	/// 直播播放地址信息
	/// </summary>
	public PlayUrlData? data { get; set; }
}
