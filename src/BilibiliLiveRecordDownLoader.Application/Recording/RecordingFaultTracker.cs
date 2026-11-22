namespace BilibiliLiveRecordDownLoader.Application.Recording;

internal sealed class RecordingFaultTracker(TimeProvider timeProvider)
{
	public static readonly TimeSpan StallThreshold = TimeSpan.FromMinutes(1);

	private long _streamWorkedAt = timeProvider.GetTimestamp();
	private TimeSpan _intermittentDuration;
	private bool _isStallReported;
	private bool _isWriteFailureReported;

	public void OnRecorded()
	{
		_streamWorkedAt = timeProvider.GetTimestamp();
		_intermittentDuration = TimeSpan.Zero;
		_isStallReported = false;
		_isWriteFailureReported = false;
	}

	// 断续送数据的线路累计满 QuickFailureThreshold 就当作正常录制过，只给流头就断开的攒不下时长，照样会提醒
	// 不重置已提醒的状态，等一次正常录制之后再计
	public void OnQuickFailure(TimeSpan activeDuration)
	{
		_intermittentDuration += activeDuration;

		if (_intermittentDuration >= RecordingAttempt.QuickFailureThreshold)
		{
			_streamWorkedAt = timeProvider.GetTimestamp();
			_intermittentDuration = TimeSpan.Zero;
		}
	}

	public void OnStreamerSilent()
	{
		_streamWorkedAt = timeProvider.GetTimestamp();
	}

	// 所有线路都试过一遍仍然失败时调用
	public bool TryReportStall()
	{
		if (_isStallReported || timeProvider.GetElapsedTime(_streamWorkedAt) < StallThreshold)
		{
			return false;
		}

		_isStallReported = true;
		return true;
	}

	// 写盘失败时线路是通的，不算线路故障
	public bool TryReportWriteFailure()
	{
		_streamWorkedAt = timeProvider.GetTimestamp();
		_isStallReported = false;

		if (_isWriteFailureReported)
		{
			return false;
		}

		_isWriteFailureReported = true;
		return true;
	}
}
