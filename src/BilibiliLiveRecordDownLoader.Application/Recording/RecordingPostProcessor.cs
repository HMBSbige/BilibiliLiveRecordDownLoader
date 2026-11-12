namespace BilibiliLiveRecordDownLoader.Application.Recording;

internal sealed partial class RecordingPostProcessor(IMediaToolAppService mediaTools, ILogger<RecordingPostProcessor> logger)
{
	public async Task ProcessAsync(string path, RecordingProfile profile)
	{
		try
		{
			FileInfo file = new(path);

			if (!file.Exists)
			{
				return;
			}

			if (file.Length is 0)
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
