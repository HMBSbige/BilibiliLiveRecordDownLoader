namespace BilibiliApi.Model.FansMedal;

public class LiveFansMedalMessage
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
	/// 粉丝徽章信息
	/// </summary>
	public LiveFansMedalData? data { get; set; }
}
