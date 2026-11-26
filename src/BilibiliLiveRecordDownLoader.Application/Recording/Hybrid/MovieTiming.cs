namespace BilibiliLiveRecordDownLoader.Application.Recording.Hybrid;

// MediaTime 为 -1 是空编辑；时长按电影时间刻度，媒体起点按轨道时间刻度
internal readonly record struct EditEntry(long Duration, long MediaTime);

// Duration 是 tkhd 的时长，按电影时间刻度
internal sealed record TrackTiming(TrackIndex Track, EditEntry[] Edits, long Duration);

// 每个样本都在输入的显示时间显示：电影从最早显示的轨道开始，其他轨道用空编辑推迟，音画相对位置和输入一样
// 样本表的解码时间从第一个样本的 0 算起，合成偏移加上 CompositionShift 写成非负的 ctts 版本 0，编辑列表的媒体起点也加上同样的量，显示时间不受影响
// 没有样本的轨道不写出，也不参与计算
internal static class MovieTiming
{
	// 退回这个值时编辑列表按毫秒取整，音画最多差一毫秒
	private const uint DefaultMovieTimescale = 1000;

	public static (uint Timescale, TrackTiming[] Tracks) Compute(IReadOnlyList<TrackIndex> tracks)
	{
		TrackIndex[] written = [.. tracks.Where(static track => track.SampleCount > 0)];
		uint timescale = GetMovieTimescale(written.Select(static track => track.Timescale));
		long start = written.Min(track => GetStart(track, timescale));
		return (timescale, [.. written.Select(track => Compute(track, start, timescale))]);
	}

	// 取各轨道时间刻度的最小公倍数，空编辑和编辑时长都能精确表示；太大时退回毫秒
	// 章节轨的样本表按毫秒，tkhd 和编辑列表按电影时间刻度；比毫秒粗时取它不小于 1000 的最小倍数，仍是各轨道时间刻度的倍数，章节起止的精度也不低于毫秒
	public static uint GetMovieTimescale(IEnumerable<uint> timescales)
	{
		long multiple = 1;

		foreach (uint timescale in timescales)
		{
			multiple = multiple / GreatestCommonDivisor(multiple, timescale) * timescale;

			if (multiple >= int.MaxValue)
			{
				return DefaultMovieTimescale;
			}
		}

		return (uint)(multiple >= DefaultMovieTimescale ? multiple : (DefaultMovieTimescale + multiple - 1) / multiple * multiple);
	}

	private static long GreatestCommonDivisor(long a, long b)
	{
		while (b is not 0)
		{
			(a, b) = (b, a % b);
		}

		return a;
	}

	private static TrackTiming Compute(TrackIndex track, long movieStart, uint movieTimescale)
	{
		long delay = GetStart(track, movieTimescale) - movieStart;
		long duration = TimeRescale.Ceiling(track.PresentationEnd - track.PresentationStart, movieTimescale, track.Timescale);
		EditEntry media = new(duration, track.PresentationStart - track.FirstDecodeTime + track.CompositionShift);
		return new TrackTiming(track, delay > 0 ? [new EditEntry(delay, -1), media] : [media], delay + duration);
	}

	private static long GetStart(TrackIndex track, uint movieTimescale)
	{
		return TimeRescale.Floor(track.PresentationStart, movieTimescale, track.Timescale);
	}
}
