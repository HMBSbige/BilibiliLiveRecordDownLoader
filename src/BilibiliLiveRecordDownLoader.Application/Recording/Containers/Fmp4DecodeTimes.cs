namespace BilibiliLiveRecordDownLoader.Application.Recording.Containers;

// tfdt 版本 0 只有 32 位，90 kHz 下约 13 小时回绕一次，timescale 不同的轨道回绕时刻也不同，要按轨道展开；展开的状态跟着读过的分片走
internal sealed class Fmp4DecodeTimes(Fmp4Track[] tracks)
{
	// 跨过回绕的相邻分片只差一个分片左右，放宽到一分钟，漏掉几个分片也认得出；差得更多的不展开，原样返回，由调用方当作源时间线重新开始
	// 不按最近的值展开：源在半个周期之后重新开始时会变成往前跳几个小时
	private const ulong WrapWindowSeconds = 60;

	private const ulong Period = 1UL << 32;

	private readonly TrackState[] _states = new TrackState[tracks.Length];

	// times 里每个带 tfdt 的 traf 一条，DecodeTime 是展开过的
	public void ReadMovieFragment(ReadOnlySpan<byte> moof, List<TrackFragmentTime> times)
	{
		times.Clear();
		int offset = 0;

		while (IsoBmff.TryReadBox(moof, ref offset, out uint type, out ReadOnlySpan<byte> traf))
		{
			if (type is BoxType.Traf && IsoBmff.ReadTrackFragmentTime(traf, offset - traf.Length, tracks) is { } time)
			{
				times.Add(moof[time.TfdtOffset] is 1 ? time : time with { DecodeTime = Unwrap(time.TrackIndex, (uint)time.DecodeTime) });
			}
		}
	}

	private ulong Unwrap(int track, uint value)
	{
		ref TrackState state = ref _states[track];

		// 32 位无符号相减按模 2³² 回绕，得到的就是向前的差
		if (state.LastDecodeTime is { } last && value < last && value - last <= WrapWindowSeconds * tracks[track].Timescale)
		{
			state.WrapBase += Period;
		}

		state.LastDecodeTime = value;
		return state.WrapBase + value;
	}

	private struct TrackState
	{
		public uint? LastDecodeTime;
		public ulong WrapBase;
	}
}
