namespace BilibiliLive.Danmu;

/// <summary>客户端每个 WebSocket 帧都重新读这些开关，连接期间也能改</summary>
public sealed class DanmuReceiveOptions
{
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
