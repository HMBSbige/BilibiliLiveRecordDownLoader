using System.Collections.Concurrent;

namespace BilibiliLive.Tests.Fakes;

// Progress<T> 在线程池上报告，复制返回时它的值可能还没送到
internal sealed class RecordingProgress : IProgress<long>
{
	public ConcurrentQueue<long> Reports { get; } = new();

	public void Report(long value)
	{
		Reports.Enqueue(value);
	}
}
