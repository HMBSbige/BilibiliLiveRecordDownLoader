namespace BilibiliLive.Danmu;

/// <summary>
/// 连接在直播状态、房间变化和连接状态之外还产出什么；客户端每个 WebSocket 帧都重新读这些开关，所以连接期间也能改
/// </summary>
public sealed class DanmuReceiveOptions
{
	/// <summary>解析弹幕、醒目留言及其删除、礼物和大航海购买</summary>
	public bool IncludeChat
	{
		get => Volatile.Read(ref field);
		set => Volatile.Write(ref field, value);
	}

	public bool IncludeRaw
	{
		get => Volatile.Read(ref field);
		set => Volatile.Write(ref field, value);
	}

	internal DanmuParseMode Mode => (IncludeChat ? DanmuParseMode.Chat : DanmuParseMode.None) | (IncludeRaw ? DanmuParseMode.Raw : DanmuParseMode.None);
}

[Flags]
internal enum DanmuParseMode
{
	None = 0,
	Chat = 1,
	Raw = 2
}
