namespace BilibiliLiveRecordDownLoader.Application.Contracts.Danmaku;

public sealed record DanmakuFileDto(string Path, DanmakuFileSourceDto Source, IReadOnlyList<DanmakuFileEntryDto> Entries, bool IsComplete, DanmakuDensityDto Density, IReadOnlyList<DanmakuHighlightDto> Highlights, string? VideoPath);

public closed record DanmakuFileSourceDto
{
	// Offset 已经加到每条记录上
	public sealed record Native(string Streamer, string Title, TimeSpan Offset) : DanmakuFileSourceDto;

	public sealed record Standard : DanmakuFileSourceDto;
}

// Counts[i] 是 [BinWidth × i, BinWidth × (i + 1)) 里的弹幕条数，覆盖到最后一条弹幕
public sealed record DanmakuDensityDto(TimeSpan BinWidth, IReadOnlyList<int> Counts);

// 文件内时间；事件里只有抽奖口令或复读时 Phrase 为空
public sealed record DanmakuHighlightDto(TimeSpan Start, TimeSpan Peak, TimeSpan End, string Phrase);

public closed record DanmakuOffsetResult
{
	public sealed record Updated(string? Subtitle) : DanmakuOffsetResult;

	// 弹幕文件已经改写，只是 ASS 没能写入
	public sealed record SubtitleNotWritten(string Subtitle, string Error) : DanmakuOffsetResult;
}
