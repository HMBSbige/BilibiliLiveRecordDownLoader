namespace BilibiliLiveRecordDownLoader.Application.Recording.Splitting;

// 设置可在录制中途改，每次都取当时的值；改标题按计数请求换文件，分段器只认自己开始之后的请求
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
