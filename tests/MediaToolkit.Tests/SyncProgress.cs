namespace MediaToolkit.Tests;

internal sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
{
	public void Report(T value)
	{
		handler(value);
	}
}
