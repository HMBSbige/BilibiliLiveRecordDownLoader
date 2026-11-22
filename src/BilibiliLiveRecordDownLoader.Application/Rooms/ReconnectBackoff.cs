namespace BilibiliLiveRecordDownLoader.Application.Rooms;

// 连续失败时间隔翻倍，抖动错开同时断线的房间；连接稳定过之后回到初始间隔
internal sealed class ReconnectBackoff(TimeSpan initialDelay, TimeProvider timeProvider, Random random)
{
	public const double MinJitter = 0.8;

	public const double MaxJitter = 1.2;

	public static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(2);

	// 弹幕服务器每 30 秒回应一次心跳，撑过一个心跳周期说明认证已经通过
	public static readonly TimeSpan StableDuration = TimeSpan.FromSeconds(30);

	private readonly TimeSpan _initialDelay = initialDelay;
	private readonly TimeSpan _maxDelay = initialDelay > MaxDelay ? initialDelay : MaxDelay;
	private TimeSpan _delay = initialDelay;
	private long _connectedAt;
	private bool _hasReceived;

	public void OnConnecting()
	{
		_connectedAt = timeProvider.GetTimestamp();
		_hasReceived = false;
	}

	public void OnReceived()
	{
		_hasReceived = true;
	}

	public TimeSpan NextDelay()
	{
		if (_hasReceived || timeProvider.GetElapsedTime(_connectedAt) >= StableDuration)
		{
			_delay = _initialDelay;
		}

		TimeSpan delay = _delay;
		_delay = _delay < _maxDelay / 2 ? _delay * 2 : _maxDelay;

		return delay * (MinJitter + (MaxJitter - MinJitter) * random.NextDouble());
	}
}
