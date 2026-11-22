namespace BilibiliLiveRecordDownLoader.Application.Recording;

internal sealed partial class RecordingPostProcessor(IMediaToolAppService mediaTools, IAppConfigManager configManager, INotificationService notifications, ILogger<RecordingPostProcessor> logger)
{
	// 刚开始录就失败的尝试只留下容器头和零星几个分片，转封装也得不到可看的内容
	internal const long MinimumLength = 64 << 10;

	// danmaku 是这一段已经写完关闭的弹幕文件，这一段没录弹幕时为 null
	public async Task ProcessAsync(string path, RecordingProfile profile, string streamer, DanmakuSegment? danmaku = null, DanmakuProfile? danmakuProfile = null)
	{
		if (await ProcessCoreAsync(path, profile, danmaku, danmakuProfile) is not { } failure)
		{
			return;
		}

		if ((await configManager.GetAsync()).NotifyOnRecordingError)
		{
			notifications.Show(AppLanguage.Current.PostProcessFailed.ToString(streamer), AppLanguage.Current.PostProcessFailedMessage.ToString(Path.GetFileName(path), failure), AppNotificationType.Error);
		}
	}

	// 和弹幕面板共用一套文案，前面加上昵称
	internal static DanmakuAssLabels SubtitleLabels => new
	(
		static (user, level, months, isRenewal) =>
		{
			string name = level switch { 1 => AppLanguage.Current.GuardLevelGovernor, 2 => AppLanguage.Current.GuardLevelAdmiral, _ => AppLanguage.Current.GuardLevelCaptain };
			string text = (months > 1, isRenewal) switch
			{
				(true, true) => AppLanguage.Current.DanmakuGuardRenewalMonthsText.ToString(name, months),
				(true, false) => AppLanguage.Current.DanmakuGuardMonthsText.ToString(name, months),
				(false, true) => AppLanguage.Current.DanmakuGuardRenewalText.ToString(name),
				(false, false) => AppLanguage.Current.DanmakuGuardText.ToString(name)
			};
			return $"{user} {text}";
		},
		static (user, gift, count) => $"{user} {AppLanguage.Current.DanmakuGiftText.ToString(gift, count)}"
	);

	internal static DanmakuChapterNames ChapterNames => new(static title => AppLanguage.Current.ChapterTitleChange.ToString(title), static phrase => phrase.Length is 0 ? AppLanguage.Current.ChapterHighlightWithoutPhrase : AppLanguage.Current.ChapterHighlight.ToString(phrase));

	// 返回失败原因；转封装任务自身的失败已由任务队列记录
	private async Task<string?> ProcessCoreAsync(string path, RecordingProfile profile, DanmakuSegment? danmaku, DanmakuProfile? danmakuProfile)
	{
		string? chapters = null;

		try
		{
			FileInfo file = new(path);

			if (!file.Exists)
			{
				return null;
			}

			string stem = RecordingFileLayout.GetStem(path);

			if (file.Length < MinimumLength)
			{
				FileOperations.TryDelete(path, logger);

				// 弹幕文件和字幕跟着视频，视频没了它们也对不上任何画面
				foreach (string companion in (ReadOnlySpan<string>)[stem + RecordingFileLayout.DanmakuExtension, stem + RecordingFileLayout.SubtitleExtension])
				{
					if (File.Exists(companion))
					{
						FileOperations.TryDelete(companion, logger);
					}
				}

				return null;
			}

			string? destination = profile.ConvertToMp4 ? RecordingFileLayout.GetMp4ConversionPath(path) : null;
			bool writeChapters = destination is not null && danmakuProfile is { WriteChapters: true };
			DanmakuFile? records = danmaku is not null && (danmakuProfile is { GenerateSubtitle: true } || writeChapters) ? await ReadDanmakuAsync(danmaku.Path) : null;

			if (records is not null && danmakuProfile is { GenerateSubtitle: true })
			{
				await WriteSubtitleAsync(stem + RecordingFileLayout.SubtitleExtension, records, danmaku?.Resolution, danmakuProfile.ShowPaidMessagesInSubtitle);
			}

			if (destination is null)
			{
				return null;
			}

			if (writeChapters && records is { Source: var source })
			{
				chapters = await WriteChaptersAsync(destination, source is DanmakuFileSource.Native { Header.Title: { Length: > 0 } title } ? title : Path.GetFileNameWithoutExtension(stem), records, danmaku?.Duration);
			}

			Task<BackgroundTaskSnapshot> remux = await mediaTools.QueueRemuxAsync(new RemuxInput(path, destination, profile.DeleteAfterConvert, false) { Chapters = chapters });

			if (await remux is { Status: BackgroundTaskStatus.Failed } failed)
			{
				return failed.Message ?? string.Empty;
			}

			return null;
		}
		catch (Exception exception)
		{
			LogPostProcessFailed(logger, exception, path);
			return exception.Message;
		}
		finally
		{
			if (chapters is not null)
			{
				FileOperations.TryDelete(chapters, logger);
			}
		}
	}

	// 弹幕文件读不了时照常转封装，只是没有字幕和章节
	private async Task<DanmakuFile?> ReadDanmakuAsync(string path)
	{
		try
		{
			return await DanmakuXmlReader.ReadFileAsync(path, CancellationToken.None);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
		{
			LogDanmakuUnreadable(logger, exception, path);
			return null;
		}
	}

	private async Task WriteSubtitleAsync(string path, DanmakuFile danmaku, VideoResolution? resolution, bool showPaidMessages)
	{
		try
		{
			await DanmakuAssGenerator.WriteFileAsync(path, danmaku.Records, resolution, showPaidMessages ? SubtitleLabels : null, CancellationToken.None);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			LogSubtitleFailed(logger, exception, path);
		}
	}

	// 时长来自时间轴，不按最后一条弹幕推，免得最后一章结束在视频结束之前；章节文件写在输出旁边，转封装之后删除
	private static async Task<string> WriteChaptersAsync(string destination, string liveTitle, DanmakuFile danmaku, TimeSpan? duration)
	{
		IReadOnlyList<DanmakuHighlight> highlights = DanmakuHighlightDetector.Detect(danmaku.Records, duration);
		IReadOnlyList<FFmpegChapter> chapters = DanmakuChapterBuilder.Build(liveTitle, danmaku.Records, highlights, duration, ChapterNames);
		string path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(destination)) ?? Path.GetTempPath(), $@".{Guid.CreateVersion7():N}.ffmetadata");
		await FFmpegMetadata.WriteChaptersFileAsync(path, chapters, CancellationToken.None);
		return path;
	}

	[LoggerMessage(Level = LogLevel.Warning, Message = "Failed to post-process {Path}")]
	private static partial void LogPostProcessFailed(ILogger logger, Exception exception, string path);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Failed to read {Path}, converting without subtitles or chapters")]
	private static partial void LogDanmakuUnreadable(ILogger logger, Exception exception, string path);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Failed to write {Path}")]
	private static partial void LogSubtitleFailed(ILogger logger, Exception exception, string path);
}
