namespace BilibiliLiveRecordDownLoader.Application.Media;

internal sealed class FFmpegProvider(IAppConfigManager configManager)
{
	public async ValueTask<FFmpegRunner?> GetRunnerAsync(CancellationToken cancellationToken = default)
	{
		AppConfig config = await configManager.GetAsync(cancellationToken);

		return FFmpegRunner.Locate(config.FFmpegPath) is { } path ? new FFmpegRunner(path) : null;
	}

	public async ValueTask<FFmpegRunner> GetRequiredRunnerAsync(CancellationToken cancellationToken = default)
	{
		return await GetRunnerAsync(cancellationToken) ?? throw new InvalidOperationException(AppLanguage.Current.FFmpegNotFound);
	}
}
