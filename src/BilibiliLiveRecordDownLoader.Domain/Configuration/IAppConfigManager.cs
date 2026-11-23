namespace BilibiliLiveRecordDownLoader.Domain.Configuration;

public interface IAppConfigManager
{
	// 这次启动时从旧版导入了配置；加载配置之后才有效
	bool IsLegacyConfigImported { get; }

	// 返回进程内共享的同一个实例，UpdateAsync 的修改立即反映在已经取得的实例上；用到时现读就能跟上设置页的修改
	ValueTask<AppConfig> GetAsync(CancellationToken cancellationToken = default);

	// 只能在 update 里修改，之后统一执行 AppConfig.ApplyDefaults，调用方不必自己规范取值
	// 读取方不加锁，可能和 update 同时执行：集合要整体替换，不能原地修改
	ValueTask UpdateAsync(Action<AppConfig> update, CancellationToken cancellationToken = default);
}
