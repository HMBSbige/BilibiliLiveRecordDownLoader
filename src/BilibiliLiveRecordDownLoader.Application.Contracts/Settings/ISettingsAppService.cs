namespace BilibiliLiveRecordDownLoader.Application.Contracts.Settings;

public interface ISettingsAppService
{
	// 加载配置之后才有效
	bool IsLegacyConfigImported { get; }

	// 返回的是快照，之后的修改不会反映在上面；要最新的值就再取一次
	Task<AppSettingsDto> GetAsync(CancellationToken cancellationToken = default);

	// update 拿到的是最新的设置，同时进行的修改不会互相覆盖；返回的设置统一规范取值后保存，调用方不必自己规范
	Task UpdateAsync(Func<AppSettingsDto, AppSettingsDto> update, CancellationToken cancellationToken = default);
}
