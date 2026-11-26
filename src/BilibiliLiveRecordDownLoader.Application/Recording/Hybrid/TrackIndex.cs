namespace BilibiliLiveRecordDownLoader.Application.Recording.Hybrid;

internal enum TrackKind
{
	Video,
	Audio
}

internal readonly record struct TimeToSample(uint Count, uint Delta);

// 一个块是文件里连续存放、样本项相同的一串样本
internal readonly record struct SampleChunk(long Offset, uint Count, uint DescriptionIndex);

// 时间按轨道的时间刻度，都在输入的显示时间线上：解码时间是按 init 段编辑列表换算过的 tfdt，显示时间是解码时间加合成偏移
// 样本表只记相邻样本的解码时间差，所以样本时长取到下一个样本的解码时间，分片之间的空隙算进前一个样本，后面样本的时间才不变；最后一个样本用自己的时长
// 视频第一个关键帧之前的样本缺参考帧，解出来是花屏，丢掉；音频每帧都能单独解码，全部保留
// 解码时间往回走（分片重叠）的样本也全部保留，解码时间改成上一个样本的加 1，样本表才能按解码顺序排，之后的样本时间不变
// 本程序录的文件里，同一分片从别的节点取到的内容逐字节相同，超过一秒的倒退在录制时已经接上，剩下的重叠只会来自源时间线不到一秒的倒退，重叠的是新内容，丢掉就少了这段音画
// 整条轨道往后挪也不行：各轨道重叠的量不同，挪完音画就错开了
internal sealed class TrackIndex(uint trackId, TrackKind kind, uint timescale, long presentationFloor, uint sampleEntryType, bool isAac, byte[] trak)
{
	private readonly List<SampleChunk> _chunks = [];
	private readonly List<RetimedSample> _retimed = [];
	private long _chunkEnd;
	private uint _lastDuration;

	public uint TrackId => trackId;

	public TrackKind Kind => kind;

	public uint Timescale => timescale;

	// stsd 里第一个样本项的类型
	public uint SampleEntryType => sampleEntryType;

	public bool IsAac => isAac;

	public byte[] Trak => trak;

	// 视频第一个关键帧之前的样本，以及数据超出文件结尾的样本
	public long DroppedSamples { get; private set; }

	public long RetimedSamples => _retimed.Count;

	public int SampleCount => Sizes.Count;

	// 负的合成偏移里绝对值最大的，每个合成偏移加上它都不是负数
	public int CompositionShift { get; private set; }

	public long FirstDecodeTime { get; private set; }

	public long LastDecodeTime { get; private set; }

	// 最早的显示时间；init 段的编辑列表不显示更早的部分
	public long PresentationStart { get; private set; } = long.MaxValue;

	// 显示时间加时长，最晚的那个
	public long PresentationEnd { get; private set; } = long.MinValue;

	public ChunkedList<uint> Sizes { get; } = new();

	public ChunkedList<int> CompositionOffsets { get; } = new();

	// 只有视频轨记录，音频样本都是同步样本
	public ChunkedList<uint> SyncSamples { get; } = new();

	public IReadOnlyList<SampleChunk> Chunks => _chunks;

	// 毫秒时间刻度的 60 fps 视频，相邻样本的解码时间差在 16 和 17 之间交替，几乎每个样本一项，所以和逐样本的表一样分块存放
	public ChunkedList<TimeToSample> TimeToSample { get; } = new();

	public long MediaDuration => LastDecodeTime + _lastDuration - FirstDecodeTime;

	public void Add(long decodeTime, uint duration, uint size, uint flags, int compositionOffset, long offset, uint descriptionIndex, long dataEnd)
	{
		bool isKey = IsoBmff.IsSyncSample(flags);

		// 录制中途写失败时最后一个分片可能不完整
		if (offset + size > dataEnd || kind is TrackKind.Video && SampleCount is 0 && !isKey)
		{
			++DroppedSamples;
			return;
		}

		if (compositionOffset < 0)
		{
			CompositionShift = Math.Max(CompositionShift, compositionOffset is int.MinValue ? int.MaxValue : -compositionOffset);
		}

		bool isRetimed = false;

		if (SampleCount is 0)
		{
			FirstDecodeTime = decodeTime;
		}
		else
		{
			if (decodeTime <= LastDecodeTime)
			{
				long delay = LastDecodeTime + 1 - decodeTime;
				decodeTime = LastDecodeTime + 1;
				_retimed.Add(new RetimedSample(SampleCount, delay, decodeTime, duration));
				isRetimed = true;
			}

			AppendDuration(decodeTime - LastDecodeTime);
		}

		LastDecodeTime = decodeTime;
		_lastDuration = duration;
		Sizes.Add(size);
		CompositionOffsets.Add(compositionOffset);

		if (kind is TrackKind.Video && isKey)
		{
			SyncSamples.Add((uint)SampleCount);
		}

		if (offset == _chunkEnd && _chunks is [.., var last] && last.DescriptionIndex == descriptionIndex)
		{
			_chunks[^1] = last with { Count = last.Count + 1 };
		}
		else
		{
			_chunks.Add(new SampleChunk(offset, 1, descriptionIndex));
		}

		_chunkEnd = offset + size;

		if (!isRetimed)
		{
			AddPresentation(decodeTime + compositionOffset, duration);
		}
	}

	// 最后一个样本的时长要等不会再有样本时才能定
	// 推后的样本尽量保持原来的显示时间，但不能早于新的解码时间减去 CompositionShift，否则合成偏移加上它还是负数；CompositionShift 要等所有样本收完才定，所以到这里才改
	// 分片里有样本却一个都留不下（比如视频全是非关键帧），或者留下的都在编辑列表不显示的范围里，转换后这条轨道就没了，不转换
	public void Complete()
	{
		if (SampleCount is 0)
		{
			if (DroppedSamples > 0)
			{
				throw new UnsupportedLayoutException($"track {trackId} has no sample to keep");
			}

			return;
		}

		AppendDuration(_lastDuration);

		foreach ((int sample, long delay, long decodeTime, uint duration) in _retimed)
		{
			int compositionOffset = (int)(Math.Max(CompositionOffsets[sample], delay - CompositionShift) - delay);
			CompositionOffsets[sample] = compositionOffset;
			AddPresentation(decodeTime + compositionOffset, duration);
		}

		PresentationStart = Math.Max(PresentationStart, presentationFloor);

		if (PresentationEnd <= PresentationStart)
		{
			throw new UnsupportedLayoutException($"track {trackId} has no sample to present");
		}
	}

	private void AddPresentation(long time, uint duration)
	{
		PresentationStart = Math.Min(PresentationStart, time);
		PresentationEnd = Math.Max(PresentationEnd, time + duration);
	}

	private void AppendDuration(long delta)
	{
		if (delta > uint.MaxValue)
		{
			throw new UnsupportedLayoutException($"track {trackId} has a gap of {delta} ticks");
		}

		if (TimeToSample is [.., { Delta: var last } entry] && last == delta)
		{
			TimeToSample[^1] = entry with { Count = entry.Count + 1 };
		}
		else
		{
			TimeToSample.Add(new TimeToSample(1, (uint)delta));
		}
	}

	// Delay 是解码时间推后的量，DecodeTime 是推后的解码时间
	private readonly record struct RetimedSample(int Sample, long Delay, long DecodeTime, uint Duration);
}
