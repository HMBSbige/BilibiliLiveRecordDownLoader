namespace BilibiliLiveRecordDownLoader.Domain.Configuration;

public interface IAppConfigManager
{
	// 这次启动时从旧版导入了配置；加载配置之后才有效
	bool IsLegacyConfigImported { get; }

	ValueTask<AppConfig> GetAsync(CancellationToken cancellationToken = default);

	ValueTask UpdateAsync(Action<AppConfig> update, CancellationToken cancellationToken = default);
}
