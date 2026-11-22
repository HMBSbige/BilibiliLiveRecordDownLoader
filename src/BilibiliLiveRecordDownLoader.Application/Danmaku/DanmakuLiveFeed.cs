namespace BilibiliLiveRecordDownLoader.Application.Danmaku;

// 弹幕连接的线程写，界面线程按序号轮询读
internal sealed class DanmakuLiveFeed(TimeProvider timeProvider)
{
	private const int RateWindowSeconds = 60;

	private readonly Lock _lock = new();
	private readonly LiveDanmakuEntryDto?[] _entries = new LiveDanmakuEntryDto?[DanmakuConsts.MaxFeedEntries];
	private readonly int[] _rate = new int[RateWindowSeconds];
	private int _head;
	private int _count;
	private long _sequence;
	private long _version;
	private long _rateSecond;
	private DanmakuConnectionState _state = DanmakuConnectionState.Connecting;
	private long? _userId;
	private bool _isMasked;
	private DateTimeOffset? _disconnectedAt;
	private bool _isRecording;

	public long Version => Interlocked.Read(ref _version);

	public DanmakuFeedStatus Status
	{
		get
		{
			lock (_lock)
			{
				AdvanceRate(CurrentSecond(timeProvider.GetTimestamp()));
				return new DanmakuFeedStatus(_state, _userId, _isMasked, _rate.Sum(), _disconnectedAt, _isRecording);
			}
		}
	}

	public RoomDanmakuSnapshot Snapshot
	{
		get
		{
			lock (_lock)
			{
				return new RoomDanmakuSnapshot(_isRecording, _userId, _isMasked);
			}
		}
	}

	public IReadOnlyList<LiveDanmakuEntryDto> GetEntries(long afterSequence)
	{
		lock (_lock)
		{
			if (_count is 0)
			{
				return [];
			}

			long first = _sequence - _count + 1;
			int skip = (int)Math.Clamp(afterSequence - first + 1, 0, _count);
			LiveDanmakuEntryDto[] result = new LiveDanmakuEntryDto[_count - skip];

			for (int i = 0; i < result.Length; ++i)
			{
				result[i] = _entries[(_head + skip + i) % _entries.Length]!;
			}

			return result;
		}
	}

	public void Add(DanmuMessage message)
	{
		if (DanmakuConverter.ToEntry(message) is not { } entry)
		{
			return;
		}

		lock (_lock)
		{
			LiveDanmakuEntryDto item = new(++_sequence, message.ReceivedAt.UtcTime, entry);

			if (_count < _entries.Length)
			{
				_entries[(_head + _count++) % _entries.Length] = item;
			}
			else
			{
				_entries[_head] = item;
				_head = (_head + 1) % _entries.Length;
			}

			long second = CurrentSecond(message.ReceivedAt.Timestamp);
			AdvanceRate(second);
			++_rate[(int)(second % RateWindowSeconds)];
			Touch();
		}
	}

	// 不再解析弹幕时清空，之后再查看不会接上一段过时的内容
	public void Clear()
	{
		lock (_lock)
		{
			Array.Clear(_entries);
			Array.Clear(_rate);
			_head = 0;
			_count = 0;
			Touch();
		}
	}

	public void SetConnecting()
	{
		lock (_lock)
		{
			_state = DanmakuConnectionState.Connecting;
			Touch();
		}
	}

	public void SetConnected(long userId)
	{
		lock (_lock)
		{
			_state = DanmakuConnectionState.Connected;
			_userId = userId;
			_isMasked = false;
			_disconnectedAt = null;
			Touch();
		}
	}

	public void SetMasked()
	{
		lock (_lock)
		{
			_isMasked = true;
			Touch();
		}
	}

	public void SetDisconnected(DateTimeOffset time)
	{
		lock (_lock)
		{
			_state = DanmakuConnectionState.Disconnected;
			_disconnectedAt ??= time;
			Touch();
		}
	}

	public bool SetRecording(bool isRecording)
	{
		lock (_lock)
		{
			if (_isRecording == isRecording)
			{
				return false;
			}

			_isRecording = isRecording;
			Touch();
			return true;
		}
	}

	private long CurrentSecond(long timestamp)
	{
		return timestamp / timeProvider.TimestampFrequency;
	}

	private void AdvanceRate(long second)
	{
		if (second <= _rateSecond)
		{
			return;
		}

		for (long s = Math.Max(_rateSecond + 1, second - RateWindowSeconds + 1); s <= second; ++s)
		{
			_rate[(int)(s % RateWindowSeconds)] = 0;
		}

		_rateSecond = second;
	}

	private void Touch()
	{
		Interlocked.Increment(ref _version);
	}
}
