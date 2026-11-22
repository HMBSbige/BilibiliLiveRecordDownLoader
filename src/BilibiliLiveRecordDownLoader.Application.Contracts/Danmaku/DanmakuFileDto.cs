namespace BilibiliLiveRecordDownLoader.Application.Contracts.Danmaku;

/// <param name="Density">弹幕条数的分布，给密度曲线用。</param>
/// <param name="VideoPath">同主干的视频，没有时为 null，导出高能片段要用它。</param>
public sealed record DanmakuFileDto(string Path, DanmakuFileSourceDto Source, IReadOnlyList<DanmakuFileEntryDto> Entries, bool IsComplete, DanmakuDensityDto Density, IReadOnlyList<DanmakuHighlightDto> Highlights, string? VideoPath);

public closed record DanmakuFileSourceDto
{
	public sealed record Native(DanmakuFileHeaderDto Header) : DanmakuFileSourceDto;

	// 其他程序写的文件，按 B 站视频弹幕的格式只读出弹幕；没有偏移，不能改写
	public sealed record Standard : DanmakuFileSourceDto;
}

// 本程序写的文件头；Title 是文件开始时的标题，Offset 已经加到每条记录上
public sealed record DanmakuFileHeaderDto(long RoomId, long ShortId, string Streamer, string Title, string Area, string ParentArea, string LiveId, DateTimeOffset? LiveStartTime, DateTimeOffset? RecordStartTime, TimeSpan Offset, string Version);

// Counts[i] 是 [BinWidth × i, BinWidth × (i + 1)) 里的弹幕条数，覆盖到最后一条记录
public sealed record DanmakuDensityDto(TimeSpan BinWidth, IReadOnlyList<int> Counts);

// 文件内时间；Phrase 是代表短语，事件里只有抽奖口令时为空
public sealed record DanmakuHighlightDto(TimeSpan Start, TimeSpan Peak, TimeSpan End, double Score, string Phrase);

public closed record DanmakuOffsetResult(DanmakuFileHeaderDto Header)
{
	// Subtitle 是重新生成的 ASS，同主干没有视频时为 null
	public sealed record Updated(DanmakuFileHeaderDto Header, string? Subtitle) : DanmakuOffsetResult(Header);

	// 弹幕文件已经改写，ASS 被播放器等程序占用没能写入；关掉占用的程序后再改一次偏移即可
	public sealed record SubtitleInUse(DanmakuFileHeaderDto Header, string Subtitle, string Error) : DanmakuOffsetResult(Header);
}
