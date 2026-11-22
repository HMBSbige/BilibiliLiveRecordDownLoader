namespace BilibiliLiveRecordDownLoader.Application.Danmaku;

internal sealed record DanmakuFileHeader
{
	public string Version { get; init; } = string.Empty;

	public long RoomId { get; init; }

	public long ShortId { get; init; }

	public string Streamer { get; init; } = string.Empty;

	// 文件开始时的直播标题
	public string Title { get; init; } = string.Empty;

	public string Area { get; init; } = string.Empty;

	public string ParentArea { get; init; } = string.Empty;

	public string LiveId { get; init; } = string.Empty;

	public DateTimeOffset? LiveStartTime { get; init; }

	public DateTimeOffset? RecordStartTime { get; init; }

	// 已经加到文件里每条记录上的时间偏移，正数让弹幕更晚出现
	public TimeSpan Offset { get; init; }
}
