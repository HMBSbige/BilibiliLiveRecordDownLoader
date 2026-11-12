namespace BilibiliLiveRecordDownLoader.Application.Recording;

internal sealed class TransferMeter : IProgress<long>
{
	private long _pending;

	public void Report(long value)
	{
		Interlocked.Add(ref _pending, value);
	}

	public long TakePending()
	{
		return Interlocked.Exchange(ref _pending, 0);
	}
}
