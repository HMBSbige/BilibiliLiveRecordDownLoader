namespace BilibiliLiveRecordDownLoader.Domain.Configuration;

public interface IAppConfigManager
{
	// 加载配置之后才有效
	bool IsLegacyConfigImported { get; }

	// 返回进程内共享的实例，UpdateAsync 的修改立即反映在上面
	ValueTask<AppConfig> GetAsync(CancellationToken cancellationToken = default);

	// 只能在 update 里修改，之后统一执行 AppConfig.ApplyDefaults
	// 读取方不加锁，可能和 update 同时执行：集合要整体替换，不能原地修改
	ValueTask UpdateAsync(Action<AppConfig> update, CancellationToken cancellationToken = default);

	// 同 UpdateAsync，但立即写盘，写不成时抛出，用于确认存下之后才能继续的操作；抛出时修改仍留在内存里
	ValueTask UpdateAndSaveAsync(Action<AppConfig> update, CancellationToken cancellationToken = default);
}
