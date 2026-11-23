namespace BilibiliLiveRecordDownLoader.Domain.Recording;

public closed record RecordingSplit
{
	public sealed record None : RecordingSplit;

	public sealed record ByDuration(TimeSpan Duration) : RecordingSplit;

	public sealed record BySize(long MaxBytes) : RecordingSplit;

	public static RecordingSplit Create(RecordingSplitMode mode, TimeSpan duration, long sizeMebibytes)
	{
		return mode switch
		{
			RecordingSplitMode.Duration => new ByDuration(duration),
			RecordingSplitMode.Size => new BySize(sizeMebibytes << 20),
			_ => new None()
		};
	}
}
