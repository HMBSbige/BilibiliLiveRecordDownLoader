namespace BilibiliLiveRecordDownLoader.Application.Tests.Fakes;

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
