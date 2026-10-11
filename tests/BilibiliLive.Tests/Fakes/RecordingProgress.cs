using System.Collections.Concurrent;

namespace BilibiliLive.Tests.Fakes;

// Progress<T> reports on the thread pool, so its values could still be in flight when the copy returns
internal sealed class RecordingProgress : IProgress<long>
{
	public ConcurrentQueue<long> Reports { get; } = new();

	public void Report(long value)
	{
		Reports.Enqueue(value);
	}
}
