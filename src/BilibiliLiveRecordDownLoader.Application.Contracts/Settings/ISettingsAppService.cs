namespace BilibiliLiveRecordDownLoader.Application.Contracts.Settings;

public interface ISettingsAppService
{
	// 加载配置之后才有效
	bool IsLegacyConfigImported { get; }

	Task<AppSettingsDto> GetAsync(CancellationToken cancellationToken = default);

	Task UpdateAsync(Func<AppSettingsDto, AppSettingsDto> update, CancellationToken cancellationToken = default);
}
