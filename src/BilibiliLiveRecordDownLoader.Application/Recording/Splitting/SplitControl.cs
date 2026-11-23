namespace BilibiliLiveRecordDownLoader.Application.Recording.Splitting;

internal sealed class SplitControl(Func<RecordingSplitPolicy> getPolicy, Func<bool> getRemoveFiller)
{
	private int _titleChanges;

	public RecordingSplitPolicy Policy => getPolicy();

	public bool RemoveFiller => getRemoveFiller();

	public int TitleChanges => Volatile.Read(ref _titleChanges);

	public void RequestTitleSplit()
	{
		Interlocked.Increment(ref _titleChanges);
	}
}
