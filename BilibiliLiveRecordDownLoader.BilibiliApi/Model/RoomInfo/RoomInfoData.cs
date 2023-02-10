namespace BilibiliApi.Model.RoomInfo;

public class RoomInfoData
{
	/// <summary>
	/// 真实房间号
	/// </summary>
	public long room_id { get; set; }

	/// <summary>
	/// 短房间号
	/// </summary>
	public long short_id { get; set; }

	/// <summary>
	/// 直播状态，
	/// 2 轮播
	/// 1 开播
	/// 0 未开播
	/// </summary>
	public long live_status { get; set; }

	/// <summary>
	/// 直播间标题
	/// </summary>
	public string? title { get; set; }
}
