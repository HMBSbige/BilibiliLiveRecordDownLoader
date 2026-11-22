namespace BilibiliLiveRecordDownLoader.Application.Rooms;

// 抖动错开同时断线的房间；初始间隔每次都重新读取，改设置不必断开连接
internal sealed class ReconnectBackoff(Func<TimeSpan> initialDelay, TimeProvider timeProvider, Random random)
{
	public const double MinJitter = 0.8;

	public const double MaxJitter = 1.2;

	public static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(2);

	// 弹幕服务器每 30 秒回应一次心跳，撑过一个心跳周期说明认证已经通过
	public static readonly TimeSpan StableDuration = TimeSpan.FromSeconds(30);

	private TimeSpan _delay = initialDelay();
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
		TimeSpan initial = initialDelay();
		TimeSpan max = initial > MaxDelay ? initial : MaxDelay;

		if (_hasReceived || timeProvider.GetElapsedTime(_connectedAt) >= StableDuration || _delay < initial)
		{
			_delay = initial;
		}
		else if (_delay > max)
		{
			_delay = max;
		}

		TimeSpan delay = _delay;
		_delay = _delay < max / 2 ? _delay * 2 : max;

		return delay * (MinJitter + (MaxJitter - MinJitter) * random.NextDouble());
	}
}
