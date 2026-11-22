namespace BilibiliLiveRecordDownLoader.Application.Recording;

// 只对需要人处理的故障各提醒一次：所有线路持续失败满一段时间，或者写不进磁盘；正常录制过一段时间后重新计
internal sealed class RecordingFaultTracker(TimeProvider timeProvider)
{
	public static readonly TimeSpan StallThreshold = TimeSpan.FromMinutes(1);

	private long _streamWorkedAt = timeProvider.GetTimestamp();
	private bool _isStallReported;
	private bool _isWriteFailureReported;

	public void OnRecorded()
	{
		_streamWorkedAt = timeProvider.GetTimestamp();
		_isStallReported = false;
		_isWriteFailureReported = false;
	}

	// 所有线路都试过一遍仍然失败时调用，返回是否需要提醒
	public bool TryReportStall()
	{
		if (_isStallReported || timeProvider.GetElapsedTime(_streamWorkedAt) < StallThreshold)
		{
			return false;
		}

		_isStallReported = true;
		return true;
	}

	// 写盘失败时线路本身是通的，不算线路故障；返回是否需要提醒
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
