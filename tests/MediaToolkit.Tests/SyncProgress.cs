namespace MediaToolkit.Tests;

// Progress<T> 把回调投递到线程池，断言时可能还没执行完
internal sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
{
	public void Report(T value)
	{
		handler(value);
	}
}
