namespace BilibiliLive.Models;

public sealed record LiveRoomInfo(long RoomId, long ShortId, long UserId, string UserName, string Title, LiveStatus LiveStatus)
{
	public string AreaName { get; init; } = string.Empty;

	public string ParentAreaName { get; init; } = string.Empty;

	/// <summary>When the current or last broadcast started; 24/7 channels report a start days or weeks ago. Null when the room has never been live.</summary>
	public DateTimeOffset? LiveStartTime { get; init; }

	/// <summary>Identifies the current or last broadcast, empty when unknown.</summary>
	public string LiveId { get; init; } = string.Empty;
}
