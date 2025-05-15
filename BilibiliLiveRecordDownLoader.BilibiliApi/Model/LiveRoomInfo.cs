using BilibiliApi.Enums;

namespace BilibiliApi.Model;

public record LiveRoomInfo
{
	/// <summary>
	/// 短房间号
	/// </summary>
	public long ShortId { get; init; }

	/// <summary>
	/// 真实房间号
	/// </summary>
	public long RoomId { get; init; }

	/// <summary>
	/// 直播状态
	/// </summary>
	public LiveStatus LiveStatus { get; set; }

	/// <summary>
	/// 直播间标题
	/// </summary>
	public string? Title { get; set; }

	/// <summary>
	/// 主播 uid
	/// </summary>
	public long UserId { get; init; }

	/// <summary>
	/// 主播名
	/// </summary>
	public string? UserName { get; set; }
}
