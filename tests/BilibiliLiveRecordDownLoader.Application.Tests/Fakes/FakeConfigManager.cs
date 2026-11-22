namespace BilibiliLiveRecordDownLoader.Application.Tests.Fakes;

internal sealed class FakeConfigManager(AppConfig? config = null) : IAppConfigManager
{
	private readonly AppConfig _config = config ?? AppConfig.CreateDefault();
	private int _updateCount;

	public int UpdateCount => Volatile.Read(ref _updateCount);

	public ValueTask<AppConfig> GetAsync(CancellationToken cancellationToken = default)
	{
		return ValueTask.FromResult(_config);
	}

	public ValueTask UpdateAsync(Action<AppConfig> update, CancellationToken cancellationToken = default)
	{
		update(_config);
		Interlocked.Increment(ref _updateCount);
		return ValueTask.CompletedTask;
	}
}
