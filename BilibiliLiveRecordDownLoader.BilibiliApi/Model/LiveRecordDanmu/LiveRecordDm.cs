namespace BilibiliApi.Model.LiveRecordDanmu;

public class LiveRecordDm
{
	/// <summary>
	/// 弹幕信息
	/// </summary>
	public LiveRecordDmInfo[]? dm_info { get; set; }

	/// <summary>
	/// 礼物弹幕信息
	/// </summary>
	public LiveRecordInteractiveInfo[]? interactive_info { get; set; }
}
