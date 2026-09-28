namespace BilibiliLive.Danmu;

public closed record DanmuMessage
{
	public sealed record LiveStarted : DanmuMessage;

	public sealed record LiveStopped(bool IsRound) : DanmuMessage;

	public sealed record RoomChanged(string Title, string? AreaName, string? ParentAreaName) : DanmuMessage;
}
