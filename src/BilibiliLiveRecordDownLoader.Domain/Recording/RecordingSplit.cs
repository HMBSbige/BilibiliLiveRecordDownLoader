namespace BilibiliLiveRecordDownLoader.Domain.Recording;

public closed record RecordingSplit
{
	public sealed record None : RecordingSplit;

	public sealed record ByDuration(TimeSpan Duration) : RecordingSplit;

	// 每段文件的硬上限，按 1024 进位，和资源管理器显示的一致
	public sealed record BySize(long MaxBytes) : RecordingSplit;

	public static RecordingSplit Create(RecordingSplitMode mode, TimeSpan duration, long sizeMegabytes)
	{
		return mode switch
		{
			RecordingSplitMode.Duration => new ByDuration(duration),
			RecordingSplitMode.Size => new BySize(sizeMegabytes << 20),
			_ => new None()
		};
	}
}
