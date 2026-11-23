namespace BilibiliLiveRecordDownLoader.Application.Recording;

internal sealed partial class RecordingPostProcessor(IMediaToolAppService mediaTools, IAppConfigManager configManager, INotificationService notifications, ILogger<RecordingPostProcessor> logger)
{
	// 刚开始录就失败的尝试只留下容器头和零星几个分片，转封装也得不到可看的内容
	internal const long MinimumLength = 64 << 10;

	private readonly Lock _lock = new();
	private readonly HashSet<Task> _pending = [];

	internal Func<string, IReadOnlyList<FFmpegChapter>, CancellationToken, Task> WriteChaptersFile { get; init; } = FFmpegMetadata.WriteChaptersFileAsync;

	// 后处理没有人等待，退出时要等它们写完字幕、删掉章节文件
	internal void Track(Task task)
	{
		if (task.IsCompleted)
		{
			return;
		}

		lock (_lock)
		{
			_pending.Add(task);
		}

		_ = task.ContinueWith(completed =>
		{
			lock (_lock)
			{
				_pending.Remove(completed);
			}
		}, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
	}

	// 后处理失败不能打断退出
	internal async Task WhenIdleAsync()
	{
		Task[] pending;

		lock (_lock)
		{
			pending = [.. _pending];
		}

		await Task.WhenAll(pending).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
	}

	// danmaku 已经写完关闭，这一段没录弹幕时为 null
	public async Task ProcessAsync(string path, RecordingProfile profile, string streamer, DanmakuSegment? danmaku, DanmakuProfile danmakuProfile)
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

	// 返回失败原因；转封装任务自身的失败已由任务队列记录
	private async Task<string?> ProcessCoreAsync(string path, RecordingProfile profile, DanmakuSegment? danmaku, DanmakuProfile danmakuProfile)
	{
		// 写之前就记下路径，写到一半失败也要删
		string? chaptersFile = null;

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
				// 录像删不掉时（比如正被别的程序打开）弹幕和字幕跟着留下
				if (FileOperations.TryDelete(path, logger))
				{
					FileOperations.TryDelete(stem + RecordingFileLayout.DanmakuExtension, logger);
					FileOperations.TryDelete(stem + RecordingFileLayout.SubtitleExtension, logger);
				}

				return null;
			}

			string? destination = profile.ConvertToMp4 ? RecordingFileLayout.GetMp4ConversionPath(path) : null;

			// 章节只能在转封装时写进 MP4
			bool writeChapters = danmakuProfile.WriteChapters && destination is not null;
			string? chapters = null;

			if (danmaku is { } segment && (danmakuProfile.GenerateSubtitle || writeChapters) && await ReadDanmakuAsync(segment.Path) is { } records)
			{
				if (danmakuProfile.GenerateSubtitle)
				{
					await WriteSubtitleAsync(stem + RecordingFileLayout.SubtitleExtension, records, segment.Resolution, danmakuProfile.ShowPaidMessagesInSubtitle);
				}

				if (writeChapters)
				{
					// 进程被强行结束时，录播目录里不会留下这个文件
					chaptersFile = Path.Combine(Path.GetTempPath(), $@"{Guid.CreateVersion7():N}.ffmetadata");
					string liveTitle = records.Source is DanmakuFileSource.Native { Header.Title: { Length: > 0 } title } ? title : Path.GetFileNameWithoutExtension(stem);
					chapters = await TryWriteChaptersAsync(chaptersFile, liveTitle, records, segment.Duration) ? chaptersFile : null;
				}
			}

			if (destination is null)
			{
				return null;
			}

			Task<BackgroundTaskSnapshot> remux = await mediaTools.QueueRemuxAsync(new RemuxInput(path, destination, profile.DeleteAfterConvert, chapters is null ? new RemuxMode.Plain() : new RemuxMode.WithChapters(chapters)));

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
			if (chaptersFile is not null)
			{
				FileOperations.TryDelete(chaptersFile, logger);
			}
		}
	}

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
			await DanmakuAssGenerator.WriteFileAsync(path, danmaku.Records, resolution, showPaidMessages ? DanmakuTexts.SubtitleLabels : null, CancellationToken.None);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			LogSubtitleFailed(logger, exception, path);
		}
	}

	// duration 来自时间轴，不按最后一条弹幕推，免得最后一章结束在视频结束之前
	private async Task<bool> TryWriteChaptersAsync(string path, string liveTitle, DanmakuFile danmaku, TimeSpan? duration)
	{
		IReadOnlyList<DanmakuHighlight> highlights = DanmakuHighlightDetector.Detect(danmaku.Records, duration);
		IReadOnlyList<FFmpegChapter> chapters = DanmakuChapterBuilder.Build(liveTitle, danmaku.Records, highlights, duration, DanmakuTexts.ChapterNames);

		try
		{
			await WriteChaptersFile(path, chapters, CancellationToken.None);
			return true;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			LogChaptersFailed(logger, exception, path);
			return false;
		}
	}

	[LoggerMessage(Level = LogLevel.Warning, Message = "Failed to post-process {Path}")]
	private static partial void LogPostProcessFailed(ILogger logger, Exception exception, string path);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Failed to read {Path}, converting without subtitles or chapters")]
	private static partial void LogDanmakuUnreadable(ILogger logger, Exception exception, string path);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Failed to write {Path}")]
	private static partial void LogSubtitleFailed(ILogger logger, Exception exception, string path);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Failed to write {Path}, converting without chapters")]
	private static partial void LogChaptersFailed(ILogger logger, Exception exception, string path);
}
