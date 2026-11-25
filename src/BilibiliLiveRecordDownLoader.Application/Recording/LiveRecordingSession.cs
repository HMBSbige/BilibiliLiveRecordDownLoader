namespace BilibiliLiveRecordDownLoader.Application.Recording;

internal sealed class LiveRecordingSession(ILogger logger)
{
	private readonly Lock _lock = new();
	private readonly HashSet<string> _reservedStems = new(StringComparer.OrdinalIgnoreCase);
	private QualityRestriction? _restriction;

	public CandidatePool CandidatePool { get; } = new(logger);

	// 后处理会在后台删除或转封装上一次的文件，只看磁盘会把同一个主干分给两次录制
	public bool TryReserveStem(string stem)
	{
		lock (_lock)
		{
			return _reservedStems.Add(stem);
		}
	}

	public bool TryUpdateRestriction(QualityRestriction? restriction)
	{
		lock (_lock)
		{
			if (_restriction == restriction)
			{
				return false;
			}

			_restriction = restriction;
			return true;
		}
	}
}
