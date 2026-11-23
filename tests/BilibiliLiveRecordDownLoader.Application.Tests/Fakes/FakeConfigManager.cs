namespace BilibiliLiveRecordDownLoader.Application.Tests.Fakes;

internal sealed class FakeConfigManager(AppConfig? config = null) : IAppConfigManager
{
	private readonly AppConfig _config = config ?? AppConfig.CreateDefault();
	private readonly Lock _lock = new();
	private int _updateCount;

	public int UpdateCount => Volatile.Read(ref _updateCount);

	public bool IsLegacyConfigImported => false;

	public ValueTask<AppConfig> GetAsync(CancellationToken cancellationToken = default)
	{
		return ValueTask.FromResult(_config);
	}

	// 和真实的实现一样逐个执行 update：设置服务整体读出再写回，同时执行会丢掉别处的修改
	public ValueTask UpdateAsync(Action<AppConfig> update, CancellationToken cancellationToken = default)
	{
		lock (_lock)
		{
			update(_config);
		}

		Interlocked.Increment(ref _updateCount);
		return ValueTask.CompletedTask;
	}
}
