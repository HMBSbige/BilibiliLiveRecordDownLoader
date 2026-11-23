namespace BilibiliLiveRecordDownLoader.Application.Danmaku;

// 弹幕连接的线程写，界面线程按序号轮询读
internal sealed class DanmakuLiveFeed(TimeProvider timeProvider)
{
	private const int RateWindowSeconds = 60;

	private readonly Lock _lock = new();
	private readonly Queue<LiveDanmakuEntryDto> _entries = new(DanmakuConsts.MaxFeedEntries);
	private readonly int[] _rate = new int[RateWindowSeconds];
	private long _sequence;
	private long _version;
	private long _rateSecond;
	// 其中的 MessagesPerMinute 随时间变化，读状态时才算
	private DanmakuFeedStatus _status = new(DanmakuConnectionState.Connecting, 0, null, false, 0, null);
	private bool _isRecording;

	public long Version => Interlocked.Read(ref _version);

	public DanmakuFeedStatus Status
	{
		get
		{
			lock (_lock)
			{
				AdvanceRate(CurrentSecond(timeProvider.GetTimestamp()));
				return _status with { MessagesPerMinute = _rate.Sum() };
			}
		}
	}

	public RoomDanmakuSnapshot Snapshot
	{
		get
		{
			lock (_lock)
			{
				return new RoomDanmakuSnapshot(_isRecording);
			}
		}
	}

	public IReadOnlyList<LiveDanmakuEntryDto> GetEntries(long afterSequence)
	{
		lock (_lock)
		{
			long first = _sequence - _entries.Count + 1;
			int skip = (int)Math.Clamp(afterSequence - first + 1, 0, _entries.Count);
			return [.. _entries.Skip(skip)];
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

			if (_entries.Count == DanmakuConsts.MaxFeedEntries)
			{
				_entries.Dequeue();
			}

			_entries.Enqueue(item);

			long second = CurrentSecond(message.ReceivedAt.Timestamp);
			AdvanceRate(second);
			++_rate[(int)(second % RateWindowSeconds)];
			Touch();
		}
	}

	public void Clear()
	{
		lock (_lock)
		{
			_entries.Clear();
			Array.Clear(_rate);
			Touch();
		}
	}

	public void SetConnecting()
	{
		lock (_lock)
		{
			_status = _status with { State = DanmakuConnectionState.Connecting };
			Touch();
		}
	}

	public void SetConnected(long userId)
	{
		lock (_lock)
		{
			_status = _status with { State = DanmakuConnectionState.Connected, Connection = _status.Connection + 1, UserId = userId, IsMasked = false, DisconnectedAt = null };
			Touch();
		}
	}

	public void SetMasked()
	{
		lock (_lock)
		{
			_status = _status with { IsMasked = true };
			Touch();
		}
	}

	public void SetDisconnected(DateTimeOffset time)
	{
		lock (_lock)
		{
			_status = _status with { State = DanmakuConnectionState.Disconnected, DisconnectedAt = _status.DisconnectedAt ?? time };
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
