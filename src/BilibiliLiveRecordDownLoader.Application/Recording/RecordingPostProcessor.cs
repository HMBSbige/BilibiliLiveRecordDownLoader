using BilibiliLiveRecordDownLoader.Application.Recording.Hybrid;
using System.Diagnostics;

namespace BilibiliLiveRecordDownLoader.Application.Recording;

internal sealed partial class RecordingPostProcessor(IMediaToolAppService mediaTools, IAppConfigManager configManager, INotificationService notifications, ILogger<RecordingPostProcessor> logger)
{
	// 刚开始录就失败的尝试只留下容器头和零星几个分片，转封装也得不到可看的内容
	internal const long MinimumLength = 64 << 10;

	private readonly Lock _lock = new();
	private readonly HashSet<Task> _pending = [];

	internal Func<string, IReadOnlyList<MediaChapter>, CancellationToken, Task> WriteChaptersFile { get; init; } = FFmpegMetadata.WriteChaptersFileAsync;

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

	internal async Task WhenIdleAsync()
	{
		Task[] pending;

		lock (_lock)
		{
			pending = [.. _pending];
		}

		await Task.WhenAll(pending).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
	}

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
	// fMP4 总是原地转成普通 MP4，不需要 FFmpeg，转换和删除原文件的设置只管 FLV 和 TS
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
				if (FileOperations.TryDelete(path, logger))
				{
					FileOperations.TryDelete(stem + RecordingFileLayout.DanmakuExtension, logger);
					FileOperations.TryDelete(stem + RecordingFileLayout.SubtitleExtension, logger);
				}

				return null;
			}

			bool isFmp4 = RecordingFileLayout.IsFmp4Recording(path);
			string? destination = isFmp4 || profile.ConvertToMp4 ? RecordingFileLayout.GetMp4ConversionPath(path) : null;
			bool writeChapters = danmakuProfile.WriteChapters && destination is not null;
			IReadOnlyList<MediaChapter>? chapters = null;

			if (danmaku is { } segment && (danmakuProfile.GenerateSubtitle || writeChapters) && await ReadDanmakuAsync(segment.Path) is { } records)
			{
				if (danmakuProfile.GenerateSubtitle)
				{
					await WriteSubtitleAsync(stem + RecordingFileLayout.SubtitleExtension, records, segment.Resolution, danmakuProfile.ShowPaidMessagesInSubtitle);
				}

				if (writeChapters)
				{
					string liveTitle = records.Source is DanmakuFileSource.Native { Header.Title: { Length: > 0 } title } ? title : Path.GetFileNameWithoutExtension(stem);
					chapters = BuildChapters(liveTitle, records, segment.Duration);
				}
			}

			if (isFmp4 && destination is not null)
			{
				return await ConvertInPlaceAsync(path, destination, chapters ?? []);
			}

			if (destination is null)
			{
				return null;
			}

			RemuxMode mode = new RemuxMode.Plain();

			if (chapters is not null)
			{
				chaptersFile = Path.Combine(Path.GetTempPath(), $"{Guid.CreateVersion7():N}.ffmetadata");

				if (await TryWriteChaptersAsync(chaptersFile, chapters))
				{
					mode = new RemuxMode.WithChapters(chaptersFile);
				}
			}

			Task<BackgroundTaskSnapshot> remux = await mediaTools.QueueRemuxAsync(new RemuxInput(path, destination, profile.DeleteAfterConvert, mode));
			return await remux is { Status: BackgroundTaskStatus.Failed } failed ? failed.Message ?? string.Empty : null;
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
			await DanmakuAssGenerator.WriteFileAsync(path, danmaku.Records, resolution, showPaidMessages ? DanmakuTexts.SubtitleLabels : null);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			LogSubtitleFailed(logger, exception, path);
		}
	}

	// 转换后的文件已经能正常播放，改不了名也不算失败
	private async Task<string?> ConvertInPlaceAsync(string path, string destination, IReadOnlyList<MediaChapter> chapters)
	{
		// 要读遍所有 moof，长录像在机械硬盘上要一会儿，不占用调用方的线程
		HybridConversionResult result = await Task.Run(() => HybridMp4Converter.Convert(path, chapters));

		switch (result)
		{
			case HybridConversionResult.Rejected { Reason: var reason }:
				LogInPlaceConversionRejected(logger, path, reason);
				return AppLanguage.Current.InPlaceConversionRejected.ToString(reason);
			case HybridConversionResult.AlreadyPlain:
				return null;
			case HybridConversionResult.Converted { WrittenBytes: var written, DroppedSamples: var dropped, RetimedSamples: var retimed }:
				LogInPlaceConversionDone(logger, path, written, dropped, retimed);
				break;
			default:
				throw new UnreachableException();
		}

		try
		{
			File.Move(path, destination);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			LogRenameFailed(logger, exception, path, destination);
		}

		return null;
	}

	// duration 来自时间轴，不按最后一条弹幕推，免得最后一章结束在视频结束之前
	private static IReadOnlyList<MediaChapter> BuildChapters(string liveTitle, DanmakuFile danmaku, TimeSpan? duration)
	{
		IReadOnlyList<DanmakuHighlight> highlights = DanmakuHighlightDetector.Detect(danmaku.Records, duration);
		return DanmakuChapterBuilder.Build(liveTitle, danmaku.Records, highlights, duration, DanmakuTexts.ChapterNames);
	}

	private async Task<bool> TryWriteChaptersAsync(string path, IReadOnlyList<MediaChapter> chapters)
	{
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

	[LoggerMessage(Level = LogLevel.Information, Message = "Converted {Path} into a regular MP4 in place, writing {Bytes} bytes, dropping {Dropped} samples and retiming {Retimed} samples that went back in time")]
	private static partial void LogInPlaceConversionDone(ILogger logger, string path, long bytes, long dropped, long retimed);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Left {Path} as fragmented MP4: {Reason}")]
	private static partial void LogInPlaceConversionRejected(ILogger logger, string path, string reason);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Failed to rename {Path} to {Destination}, keeping the recorded name")]
	private static partial void LogRenameFailed(ILogger logger, Exception exception, string path, string destination);
}
