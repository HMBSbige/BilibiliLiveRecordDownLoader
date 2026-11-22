using System.Buffers.Binary;

namespace BilibiliLiveRecordDownLoader.Application.Recording.Splitting;

// 各轨道用同一个起点，按 TimeSpan 的刻度计时再按各自的 timescale 换算，各自归零的话音画会错开
// 每个 moof 推进一次，和 Fmp4HlsSplitter 报告给时间轴的媒体时间一一对应
internal sealed class Fmp4Rebaser
{
	private readonly List<TrackFragmentTime> _fragment = [];
	private Fmp4TrackTimes _times = new([]);
	private SegmentClock _clock = NewClock();

	public void StartSegment()
	{
		_clock = NewClock();
	}

	// 每次提交都是从分片开头开始的完整分片
	public void Rebase(Span<byte> elements)
	{
		int offset = 0;

		while (IsoBmff.TryReadBox(elements, ref offset, out ReadOnlySpan<byte> type, out ReadOnlySpan<byte> content))
		{
			if (type.SequenceEqual("moov"u8))
			{
				_times = new Fmp4TrackTimes(IsoBmff.ReadTracks(content));
			}
			else if (type.SequenceEqual("moof"u8) && _times.ReadMovieFragment(content, _fragment) is { } span)
			{
				RebaseMovieFragment(elements.Slice(offset - content.Length, content.Length), _clock.Advance(span.Start.Ticks, span.End.Ticks));
			}
		}
	}

	private static SegmentClock NewClock()
	{
		return new SegmentClock(MediaSpan.TimelineRestartTolerance.Ticks);
	}

	// 比起点略早的轨道夹到 0；版本 0 的字段只有 32 位，超出时和源一样回绕
	private void RebaseMovieFragment(Span<byte> moof, long offset)
	{
		foreach (TrackFragmentTime time in _fragment)
		{
			Span<byte> tfdt = moof.Slice(time.TfdtOffset);
			ulong rebased = (ulong)Math.Max((long)Math.Min(time.DecodeTime, long.MaxValue) - time.Track.FromTicksCeiling(offset), 0);

			if (tfdt[0] is 1)
			{
				BinaryPrimitives.WriteUInt64BigEndian(tfdt.Slice(4), rebased);
			}
			else
			{
				BinaryPrimitives.WriteUInt32BigEndian(tfdt.Slice(4), (uint)rebased);
			}
		}
	}
}
