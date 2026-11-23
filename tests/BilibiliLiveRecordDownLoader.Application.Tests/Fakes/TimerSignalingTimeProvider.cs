namespace BilibiliLiveRecordDownLoader.Application.Tests.Fakes;

// 定时器建好之后再拨钟，否则拨过的时间算不到它身上
internal sealed class TimerSignalingTimeProvider : FakeTimeProvider
{
	public TaskCompletionSource TimerCreated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

	public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
	{
		ITimer timer = base.CreateTimer(callback, state, dueTime, period);
		TimerCreated.TrySetResult();
		return timer;
	}
}
