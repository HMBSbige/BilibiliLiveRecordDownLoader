namespace BilibiliLiveRecordDownLoader.Application.Recording.Splitting;

// 按分片给出时间轴用的媒体时间；状态跟着读过的分片走，要比较的两边各用一个实例读同样的分片
internal sealed class Fmp4TrackTimes(Fmp4Track[] tracks)
{
	// 一个 moof 只有几秒；trun 损坏时算出的时长可能大得离谱，样本都用默认时长时 sample_count 不受 trun 长度约束，能有几百天；按它定结尾会把之后的时间都推远
	private const ulong MaxFragmentSeconds = 60;

	// 回绕窗口外的跳变由 SegmentClock 当作重新开始
	private readonly Fmp4DecodeTimes _decodeTimes = new(tracks);
	private readonly bool[] _hasStarted = new bool[tracks.Length];
	private readonly List<TrackFragmentTime> _fragment = [];
	private readonly List<MediaSpan> _spans = [];

	public MediaSpan[] ReadFragmentSpans(ReadOnlySpan<byte> fragment)
	{
		_spans.Clear();
		int offset = 0;

		while (IsoBmff.TryReadBox(fragment, ref offset, out uint type, out ReadOnlySpan<byte> moof))
		{
			if (type is BoxType.Moof && ReadMovieFragment(moof, _fragment) is { } span)
			{
				_spans.Add(span);
			}
		}

		return [.. _spans];
	}

	// times 里每个带 tfdt 的 traf 一条，DecodeTime 是展开过的；用解码时间，不看合成时间偏移，只差几帧
	// 没有编辑列表的分片 MP4 里，比最早的轨道晚开始的轨道可能把第一个样本挪到 0、时长拉长到下一个样本；按它定开头会被当作源时间线重新开始
	// 所以第一次出现的轨道只在分片里没有已经出现过的轨道时才参与定开头
	public MediaSpan? ReadMovieFragment(ReadOnlySpan<byte> moof, List<TrackFragmentTime> times)
	{
		_decodeTimes.ReadMovieFragment(moof, times);
		long? start = null;
		long? firstStart = null;
		long end = long.MinValue;

		foreach (TrackFragmentTime time in times)
		{
			long trackStart = time.Track.ToTicks(time.DecodeTime);

			if (_hasStarted[time.TrackIndex])
			{
				start = Math.Min(start ?? long.MaxValue, trackStart);
			}
			else
			{
				firstStart = Math.Min(firstStart ?? long.MaxValue, trackStart);
			}

			ulong duration = time.Duration <= MaxFragmentSeconds * time.Track.Timescale ? time.Duration : 0;
			end = Math.Max(end, time.Track.ToTicks(time.DecodeTime + duration));
		}

		foreach (TrackFragmentTime time in times)
		{
			_hasStarted[time.TrackIndex] = true;
		}

		return (start ?? firstStart) is { } value ? new MediaSpan(TimeSpan.FromTicks(value), TimeSpan.FromTicks(end)) : null;
	}
}
