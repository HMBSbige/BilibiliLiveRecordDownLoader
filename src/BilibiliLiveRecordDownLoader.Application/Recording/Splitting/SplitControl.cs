namespace BilibiliLiveRecordDownLoader.Application.Recording.Splitting;

// 分段设置和标题都可能在录制中途改：分段器每到一个切分点取一次当时的设置，每写出一个单元取一次是否去掉填充数据；改标题按版本号请求换文件，分段器只认自己开始之后的请求
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
