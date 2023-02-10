namespace BilibiliApi.Enums;

public enum DanmuCommand
{
	/// <summary>
	/// 未知
	/// </summary>
	Unknown,

	/// <summary>
	/// 弹幕消息
	/// </summary>
	DANMU_MSG,

	/// <summary>
	/// 有人进入直播间
	/// </summary>
	INTERACT_WORD,

	/// <summary>
	/// 开播、收到推流
	/// </summary>
	LIVE,

	/// <summary>
	/// 下播
	/// </summary>
	PREPARING,

	/// <summary>
	/// 房间信息改变
	/// </summary>
	ROOM_CHANGE,

	/// <summary>
	/// 投喂礼物
	/// </summary>
	SEND_GIFT,

	/// <summary>
	/// 连击礼物
	/// </summary>
	COMBO_SEND,

	/// <summary>
	/// 广播
	/// </summary>
	NOTICE_MSG,

	/// <summary>
	/// 粉丝关注变动
	/// </summary>
	ROOM_REAL_TIME_MESSAGE_UPDATE,

	/// <summary>
	/// 小时榜变动
	/// </summary>
	ACTIVITY_BANNER_UPDATE_V2,

	/// <summary>
	/// 欢迎老爷
	/// </summary>
	WELCOME_GUARD,

	/// <summary>
	/// 欢迎舰长
	/// </summary>
	ENTRY_EFFECT,

	/// <summary>
	/// 上舰
	/// </summary>
	GUARD_BUY,

	/// <summary>
	/// 续费了舰长
	/// </summary>
	USER_TOAST_MSG,

	/// <summary>
	/// SC
	/// </summary>
	SUPER_CHAT_MESSAGE,

	/// <summary>
	/// SC
	/// </summary>
	SUPER_CHAT_MESSAGE_JPN,
}
