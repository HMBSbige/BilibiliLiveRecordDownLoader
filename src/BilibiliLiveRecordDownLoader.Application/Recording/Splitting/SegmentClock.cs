namespace BilibiliLiveRecordDownLoader.Application.Recording.Splitting;

// 分割器改写时间戳、时间轴放弹幕各用一个实例，要按同样的元素、同样的顺序推进才对得上；容差和时间用同一个单位
internal struct SegmentClock(long tolerance)
{
	// 源时间减去它就是文件时间
	public long? Offset { get; private set; }

	public long Last { get; private set; }

	public void Begin(long origin)
	{
		Offset ??= origin;
	}

	// 按开头判断倒退：按结尾的话，长的 moof 要倒退超过容差加它自己的时长才认得出
	// 前提是元素里各轨道覆盖同一段时间；音视频分在不同的 moof 里且单个超过容差时会被当成重新开始，那样要按轨道分别记已经到达的时间
	public long Advance(long start, long end)
	{
		long offset = Offset ?? start;

		if (start - offset < Last - tolerance)
		{
			offset = start - Last;
		}

		Offset = offset;
		Last = Math.Max(Last, end - offset);
		return offset;
	}

	// 流头、序列头这类不带媒体时间的元素
	public readonly long Place(long time)
	{
		long placed = Offset is { } offset ? time - offset : 0;
		return placed < Last - tolerance ? Last : placed;
	}
}
