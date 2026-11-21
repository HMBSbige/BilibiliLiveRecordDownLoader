namespace BilibiliLiveRecordDownLoader.Application.Recording;

internal sealed partial class RecordingPostProcessor(IMediaToolAppService mediaTools, ILogger<RecordingPostProcessor> logger)
{
	// 刚开始录就失败的尝试只留下容器头和零星几个分片，转封装也得不到可看的内容
	internal const long MinimumLength = 64 << 10;

	public async Task ProcessAsync(string path, RecordingProfile profile)
	{
		try
		{
			FileInfo file = new(path);

			if (!file.Exists)
			{
				return;
			}

			if (file.Length < MinimumLength)
			{
				FileOperations.TryDelete(path, logger);
				return;
			}

			if (profile.ConvertToMp4 && RecordingFileLayout.GetMp4ConversionPath(path) is { } destination)
			{
				await mediaTools.QueueRemuxAsync(new RemuxInput(path, destination, profile.DeleteAfterConvert, false));
			}
		}
		catch (Exception exception)
		{
			LogPostProcessFailed(logger, exception, path);
		}
	}

	[LoggerMessage(Level = LogLevel.Warning, Message = "Failed to post-process {Path}")]
	private static partial void LogPostProcessFailed(ILogger logger, Exception exception, string path);
}
