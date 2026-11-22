namespace BilibiliLiveRecordDownLoader.Application.Tests.Fakes;

// 主循环已经停止的 UI 线程
internal sealed class StoppedSynchronizationContext : SynchronizationContext
{
	private int _posted;

	public int Posted => Volatile.Read(ref _posted);

	// 测试自己的 await 不能捕获这个上下文，调用完立即换回来
	public T Run<T>(Func<T> action)
	{
		SynchronizationContext? previous = Current;
		SetSynchronizationContext(this);

		try
		{
			return action();
		}
		finally
		{
			SetSynchronizationContext(previous);
		}
	}

	public void Run(Action action)
	{
		Run(() =>
		{
			action();
			return 0;
		});
	}

	public override void Post(SendOrPostCallback d, object? state)
	{
		Interlocked.Increment(ref _posted);
	}

	public override void Send(SendOrPostCallback d, object? state)
	{
		Interlocked.Increment(ref _posted);
	}
}
