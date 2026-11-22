namespace BilibiliLiveRecordDownLoader.Application.Recording;

internal sealed partial class RecordingPostProcessor(IMediaToolAppService mediaTools, IAppConfigManager configManager, INotificationService notifications, ILogger<RecordingPostProcessor> logger)
{
	// 刚开始录就失败的尝试只留下容器头和零星几个分片，转封装也得不到可看的内容
	internal const long MinimumLength = 64 << 10;

	public async Task ProcessAsync(string path, RecordingProfile profile, string streamer)
	{
		if (await ProcessCoreAsync(path, profile) is not { } failure)
		{
			return;
		}

		if ((await configManager.GetAsync()).NotifyOnRecordingError)
		{
			notifications.Show(AppLanguage.Current.PostProcessFailed.ToString(streamer), AppLanguage.Current.PostProcessFailedMessage.ToString(Path.GetFileName(path), failure), AppNotificationType.Error);
		}
	}

	// 返回失败原因；转封装任务自身的失败已由任务队列记录
	private async Task<string?> ProcessCoreAsync(string path, RecordingProfile profile)
	{
		try
		{
			FileInfo file = new(path);

			if (!file.Exists)
			{
				return null;
			}

			if (file.Length < MinimumLength)
			{
				FileOperations.TryDelete(path, logger);
				return null;
			}

			if (profile.ConvertToMp4 && RecordingFileLayout.GetMp4ConversionPath(path) is { } destination)
			{
				Task<BackgroundTaskSnapshot> remux = await mediaTools.QueueRemuxAsync(new RemuxInput(path, destination, profile.DeleteAfterConvert, false));

				if (await remux is { Status: BackgroundTaskStatus.Failed } failed)
				{
					return failed.Message ?? string.Empty;
				}
			}

			return null;
		}
		catch (Exception exception)
		{
			LogPostProcessFailed(logger, exception, path);
			return exception.Message;
		}
	}

	[LoggerMessage(Level = LogLevel.Warning, Message = "Failed to post-process {Path}")]
	private static partial void LogPostProcessFailed(ILogger logger, Exception exception, string path);
}
