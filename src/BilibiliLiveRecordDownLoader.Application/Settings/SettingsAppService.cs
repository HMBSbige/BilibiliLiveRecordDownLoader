namespace BilibiliLiveRecordDownLoader.Application.Settings;

internal sealed class SettingsAppService(IAppConfigManager configManager) : ISettingsAppService
{
	public bool IsLegacyConfigImported => configManager.IsLegacyConfigImported;

	public async Task<AppSettingsDto> GetAsync(CancellationToken cancellationToken = default)
	{
		return (await configManager.GetAsync(cancellationToken)).ToSettingsDto();
	}

	public Task UpdateAsync(Func<AppSettingsDto, AppSettingsDto> update, CancellationToken cancellationToken = default)
	{
		return configManager.UpdateAsync(config => config.Apply(update(config.ToSettingsDto())), cancellationToken).AsTask();
	}
}
