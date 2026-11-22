namespace BilibiliLive.Danmu;

/// <summary>
/// What a connection yields beyond the live status, room changes and connection state; the client reads the flags for every WebSocket frame, so they can change while connected.
/// </summary>
public sealed class DanmuReceiveOptions
{
	private volatile bool _includeChat;
	private volatile bool _includeRaw;

	/// <summary>Parses comments, super chats and their deletions, gifts and guard purchases.</summary>
	public bool IncludeChat
	{
		get => _includeChat;
		set => _includeChat = value;
	}

	public bool IncludeRaw
	{
		get => _includeRaw;
		set => _includeRaw = value;
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
