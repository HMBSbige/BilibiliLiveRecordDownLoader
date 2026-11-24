namespace BilibiliLiveRecordDownLoader.Application.Tests.Fakes;

internal sealed class FakeConfigManager(AppConfig? config = null) : IAppConfigManager
{
	private readonly AppConfig _config = config ?? AppConfig.CreateDefault();
	private readonly Lock _lock = new();
	private int _updateCount;
	private int _saveCount;

	public int UpdateCount => Volatile.Read(ref _updateCount);

	public int SaveCount => Volatile.Read(ref _saveCount);

	public Exception? SaveFailure { get; set; }

	public bool IsLegacyConfigImported => false;

	public ValueTask<AppConfig> GetAsync(CancellationToken cancellationToken = default)
	{
		// ReSharper disable once InconsistentlySynchronizedField
		return ValueTask.FromResult(_config);
	}

	// 逐个执行：设置服务整体读出再写回，同时执行会丢掉别处的修改
	public ValueTask UpdateAsync(Action<AppConfig> update, CancellationToken cancellationToken = default)
	{
		lock (_lock)
		{
			update(_config);
			_config.ApplyDefaults();
		}

		Interlocked.Increment(ref _updateCount);
		return ValueTask.CompletedTask;
	}

	public async ValueTask UpdateAndSaveAsync(Action<AppConfig> update, CancellationToken cancellationToken = default)
	{
		await UpdateAsync(update, cancellationToken);

		if (SaveFailure is { } failure)
		{
			throw failure;
		}

		Interlocked.Increment(ref _saveCount);
	}
}
