using System.Collections.Concurrent;

namespace BilibiliLive.Tests.Fakes;

internal sealed class RecordingProgress : IProgress<long>
{
	public ConcurrentQueue<long> Reports { get; } = new();

	public void Report(long value)
	{
		Reports.Enqueue(value);
	}
}
