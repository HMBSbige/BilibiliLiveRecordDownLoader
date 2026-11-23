namespace BilibiliLiveRecordDownLoader.Application.Contracts.Danmaku;

/// <param name="VideoPath">同主干的视频</param>
public sealed record DanmakuFileDto(string Path, DanmakuFileSourceDto Source, IReadOnlyList<DanmakuFileEntryDto> Entries, bool IsComplete, DanmakuDensityDto Density, IReadOnlyList<DanmakuHighlightDto> Highlights, string? VideoPath);

public closed record DanmakuFileSourceDto
{
	// Title 是文件开始时的标题，Offset 已经加到每条记录上
	public sealed record Native(string Streamer, string Title, TimeSpan Offset) : DanmakuFileSourceDto;

	// 其他程序写的文件只读出弹幕，不能改写
	public sealed record Standard : DanmakuFileSourceDto;
}

// Counts[i] 是 [BinWidth × i, BinWidth × (i + 1)) 里的弹幕条数，覆盖到最后一条弹幕
public sealed record DanmakuDensityDto(TimeSpan BinWidth, IReadOnlyList<int> Counts);

// 文件内时间；Phrase 是代表短语，事件里只有抽奖口令或复读时为空
public sealed record DanmakuHighlightDto(TimeSpan Start, TimeSpan Peak, TimeSpan End, string Phrase);

public closed record DanmakuOffsetResult
{
	// Subtitle 是重新生成的 ASS，同主干没有视频时为 null
	public sealed record Updated(string? Subtitle) : DanmakuOffsetResult;

	// 弹幕文件已经改写，ASS 没能写入；排除原因后再改一次偏移即可
	public sealed record SubtitleNotWritten(string Subtitle, string Error) : DanmakuOffsetResult;
}
