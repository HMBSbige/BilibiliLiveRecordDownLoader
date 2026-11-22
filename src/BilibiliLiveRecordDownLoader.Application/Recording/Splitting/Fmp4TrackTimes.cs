namespace BilibiliLiveRecordDownLoader.Application.Recording.Splitting;

// tfdt 版本 0 只有 32 位，90 kHz 下约 13 小时回绕一次，timescale 不同的轨道回绕时刻也不同，要按轨道展开
// 展开的状态跟着读过的分片走，要比较的两边各用一个实例读同样的分片
internal sealed class Fmp4TrackTimes(Fmp4Track[] tracks)
{
	// 跨过回绕的相邻分片只差一个分片左右，放宽到一分钟，漏掉几个分片也认得出；差得更多的当作源时间线重新开始，交给 SegmentClock
	// 不按最近的值展开：源在半个周期之后重新开始时会变成往前跳几个小时，SegmentClock 不处理往前跳
	private const ulong WrapWindowSeconds = 60;

	private const ulong Period = 1UL << 32;

	private readonly Wrap[] _wraps = new Wrap[tracks.Length];
	private readonly List<TrackFragmentTime> _fragment = [];
	private readonly List<MediaSpan> _spans = [];

	// 每个 moof 一条，和 Fmp4Rebaser 推进时间的粒度相同，时间轴才能认出 moof 之间的重新开始
	public MediaSpan[] ReadFragmentSpans(ReadOnlySpan<byte> fragment)
	{
		_spans.Clear();
		int offset = 0;

		while (IsoBmff.TryReadBox(fragment, ref offset, out ReadOnlySpan<byte> type, out ReadOnlySpan<byte> moof))
		{
			if (type.SequenceEqual("moof"u8) && ReadMovieFragment(moof, _fragment) is { } span)
			{
				_spans.Add(span);
			}
		}

		return [.. _spans];
	}

	// times 里的 TfdtOffset 换成相对 moof 内容的位置；用解码时间，不看合成时间偏移，只差几帧
	public MediaSpan? ReadMovieFragment(ReadOnlySpan<byte> moof, List<TrackFragmentTime> times)
	{
		times.Clear();
		long? start = null;
		long end = long.MinValue;
		int offset = 0;

		while (IsoBmff.TryReadBox(moof, ref offset, out ReadOnlySpan<byte> type, out ReadOnlySpan<byte> traf))
		{
			if (!type.SequenceEqual("traf"u8) || IsoBmff.ReadTrackFragmentTime(traf, tracks) is not { } time)
			{
				continue;
			}

			ulong decodeTime = traf[time.TfdtOffset] is 1 ? time.DecodeTime : Unwrap(time.TrackIndex, (uint)time.DecodeTime);
			time = time with { DecodeTime = decodeTime, TfdtOffset = offset - traf.Length + time.TfdtOffset };
			times.Add(time);
			start = Math.Min(start ?? long.MaxValue, time.Track.ToTicks(time.DecodeTime));
			end = Math.Max(end, time.Track.ToTicks(time.DecodeTime + time.Duration));
		}

		return start is { } value ? new MediaSpan(TimeSpan.FromTicks(value), TimeSpan.FromTicks(end)) : null;
	}

	private ulong Unwrap(int track, uint value)
	{
		ref Wrap wrap = ref _wraps[track];

		// 32 位无符号相减按模 2³² 回绕，得到的就是向前的差
		if (wrap.Last is { } last && value < last && value - last <= WrapWindowSeconds * tracks[track].Timescale)
		{
			wrap.Base += Period;
		}

		wrap.Last = value;
		return wrap.Base + value;
	}

	private struct Wrap
	{
		public uint? Last;
		public ulong Base;
	}
}
