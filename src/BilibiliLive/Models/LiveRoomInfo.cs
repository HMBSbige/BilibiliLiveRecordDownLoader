namespace BilibiliLive.Models;

public sealed record LiveRoomInfo(long RoomId, long ShortId, long UserId, string UserName, string Title, LiveStatus LiveStatus)
{
	public string AreaName { get; init; } = string.Empty;

	public string ParentAreaName { get; init; } = string.Empty;

	/// <summary>本场或上一场直播的开始时间，24 小时直播的频道可能是几天甚至几周前；从未开播的房间为 null</summary>
	public DateTimeOffset? LiveStartTime { get; init; }

	/// <summary>本场或上一场直播的标识，未知时为空</summary>
	public string LiveId { get; init; } = string.Empty;
}
