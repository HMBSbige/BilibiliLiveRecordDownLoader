namespace BilibiliLiveRecordDownLoader.Application.Danmaku;

internal sealed partial class DanmakuAppService(RoomAppService rooms, IMediaToolAppService mediaTools, IBackgroundTaskAppService tasks, IAppConfigManager configManager, ILogger<DanmakuAppService> logger) : IDanmakuAppService
{
	internal const int MaxDensityBins = 2000;

	internal static readonly TimeSpan MinDensityBinWidth = TimeSpan.FromSeconds(5);

	public IDanmakuFeed Subscribe(long roomId)
	{
		if (rooms.FindSession(roomId) is not { } session)
		{
			throw new KeyNotFoundException($@"Room {roomId} is not monitored.");
		}

		return new Feed(roomId, session.Danmaku.Feed, session.Danmaku.Subscribe());
	}

	// 解析和 ASS 排版是 CPU 工作，不能让 await 回到调用方的 UI 线程上执行
	public Task<DanmakuFileDto> OpenFileAsync(string path, CancellationToken cancellationToken = default)
	{
		return Task.Run(() => OpenFileCoreAsync(path, cancellationToken), cancellationToken);
	}

	public Task<DanmakuOffsetResult> ChangeOffsetAsync(string path, TimeSpan offset, CancellationToken cancellationToken = default)
	{
		return Task.Run(() => ChangeOffsetCoreAsync(path, offset, cancellationToken), cancellationToken);
	}

	public async Task<string> QueueHighlightExportAsync(string danmakuPath, DanmakuHighlightDto highlight, CancellationToken cancellationToken = default)
	{
		if (FindVideo(danmakuPath) is not { } video)
		{
			throw new FileNotFoundException(AppLanguage.Current.DanmakuVideoNotFound.ToString(Path.GetFileName(danmakuPath)), danmakuPath);
		}

		TimeSpan start = highlight.Start - DanmakuConsts.HighlightExportMargin;
		start = start > TimeSpan.Zero ? start : TimeSpan.Zero;
		TimeSpan end = highlight.End + DanmakuConsts.HighlightExportMargin;
		// 排着队的剪辑还没写出文件，也要避开它的输出，否则后一个失败会删掉前一个的成果
		IReadOnlyList<BackgroundTaskSnapshot> pending = [.. tasks.GetTasks().Where(static task => task.Status is BackgroundTaskStatus.Queued or BackgroundTaskStatus.Running)];
		string destination = GetHighlightPath(RecordingFileLayout.GetStem(danmakuPath), video, start, path => File.Exists(path) || pending.Any(task => task.TargetPath is { } target && FileOperations.IsSamePath(target, path)));
		await mediaTools.QueueCutAsync(new CutInput(video, destination, start, end), cancellationToken);
		return destination;
	}

	// 录制文件的主干不含 .highlight- 后缀，按主干找视频、判断主干是否占用都不会把片段当成录像
	// 主干从弹幕文件取：标题以 _fmp4 结尾时，转封装后的 .mp4 看起来和 fMP4 录制文件一样，从视频路径推不出主干
	internal static string GetHighlightPath(string stem, string video, TimeSpan start, Func<string, bool> isTaken)
	{
		string extension = Path.GetExtension(video).ToLowerInvariant() switch
		{
			@".flv" => @".flv",
			@".ts" => @".ts",
			_ => @".mp4"
		};
		string name = string.Create(CultureInfo.InvariantCulture, $@"{stem}.highlight-{(int)start.TotalHours:00}{start.Minutes:00}{start.Seconds:00}");

		for (int sequence = 1; ; ++sequence)
		{
			string path = sequence is 1 ? name + extension : string.Create(CultureInfo.InvariantCulture, $@"{name}-{sequence}{extension}");

			if (!isTaken(path))
			{
				return path;
			}
		}
	}

	internal static string? FindVideo(string danmakuPath)
	{
		return RecordingFileLayout.GetVideoPaths(RecordingFileLayout.GetStem(danmakuPath)).FirstOrDefault(File.Exists);
	}

	private static async Task<DanmakuFileDto> OpenFileCoreAsync(string path, CancellationToken cancellationToken)
	{
		DanmakuFile file = await DanmakuXmlReader.ReadFileAsync(path, cancellationToken);

		// 自己的文件录制刚开始、还没有记录时也是弹幕文件
		if (file is { Records.Count: 0, IsComplete: false, Source: DanmakuFileSource.Standard })
		{
			throw new InvalidDataException(AppLanguage.Current.InvalidDanmakuFile.ToString(path));
		}

		DanmakuFileSourceDto source = file.Source switch
		{
			DanmakuFileSource.Native { Header: var header } => new DanmakuFileSourceDto.Native(header.Streamer, header.Title, header.Offset),
			DanmakuFileSource.Standard => new DanmakuFileSourceDto.Standard()
		};

		DanmakuRecord[] records = [.. file.Records.OrderBy(static record => record.Time)];
		IReadOnlyList<DanmakuHighlight> highlights = DanmakuHighlightDetector.Detect(records);

		return new DanmakuFileDto
		(
			path,
			source,
			[.. records.Select(DanmakuConverter.ToEntry)],
			file.IsComplete,
			ComputeDensity(records),
			[.. highlights.Select(DanmakuConverter.ToDto)],
			FindVideo(path)
		);
	}

	private async Task<DanmakuOffsetResult> ChangeOffsetCoreAsync(string path, TimeSpan offset, CancellationToken cancellationToken)
	{
		// Linux 上替换正在写的文件不会失败，写入者会接着写进已经删除的旧文件
		if (rooms.IsRecordingDanmakuFile(path))
		{
			throw new IOException(AppLanguage.Current.DanmakuFileInUse.ToString(path));
		}

		try
		{
			await DanmakuOffsetRewriter.RewriteAsync(path, DanmakuDefaults.ClampOffset(offset), cancellationToken);
		}
		catch (InvalidDataException exception)
		{
			throw new InvalidDataException(AppLanguage.Current.DanmakuFileNotRewritable, exception);
		}

		if (FindVideo(path) is null)
		{
			return new DanmakuOffsetResult.Updated(null);
		}

		// 弹幕文件已经替换，中途取消会留下偏移和弹幕文件对不上的字幕
		string subtitle = RecordingFileLayout.GetStem(path) + RecordingFileLayout.SubtitleExtension;
		DanmakuFile file = await DanmakuXmlReader.ReadFileAsync(path, CancellationToken.None);
		VideoResolution? resolution = await ReadResolutionAsync(path, CancellationToken.None);
		DanmakuAssLabels? labels = (await configManager.GetAsync(CancellationToken.None)).ShowPaidMessagesInSubtitle ? DanmakuTexts.SubtitleLabels : null;

		try
		{
			await DanmakuAssGenerator.WriteFileAsync(subtitle, file.Records, resolution, labels, CancellationToken.None);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			LogSubtitleNotWritten(logger, exception, subtitle);
			return new DanmakuOffsetResult.SubtitleNotWritten(subtitle, exception.Message);
		}

		return new DanmakuOffsetResult.Updated(subtitle);
	}

	internal static DanmakuDensityDto ComputeDensity(IReadOnlyList<DanmakuRecord> records)
	{
		DanmakuRecord.Comment[] comments = [.. records.OfType<DanmakuRecord.Comment>()];

		if (comments.Length is 0)
		{
			return new DanmakuDensityDto(MinDensityBinWidth, []);
		}

		long last = comments.Max(static comment => comment.Time.Ticks);

		// 按整秒放宽，曲线的刻度好读
		TimeSpan width = TimeSpan.FromSeconds(Math.Max(MinDensityBinWidth.TotalSeconds, Math.Floor((double)last / TimeSpan.TicksPerSecond / MaxDensityBins) + 1));

		int[] counts = new int[(int)(last / width.Ticks) + 1];

		foreach (DanmakuRecord.Comment comment in comments)
		{
			++counts[(int)(comment.Time.Ticks / width.Ticks)];
		}

		return new DanmakuDensityDto(width, counts);
	}

	// 转封装进行中的 .mp4 还没写 moov，读不到尺寸，这时源录像还在，接着试下一个
	private async Task<VideoResolution?> ReadResolutionAsync(string danmakuPath, CancellationToken cancellationToken)
	{
		foreach (string video in RecordingFileLayout.GetVideoPaths(RecordingFileLayout.GetStem(danmakuPath)).Where(File.Exists))
		{
			try
			{
				if (await VideoResolutionProbe.ReadAsync(video, cancellationToken) is { } resolution)
				{
					return resolution;
				}
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				LogResolutionUnreadable(logger, exception, video);
			}
		}

		return null;
	}

	// 房间移除之后订阅仍然可用，只是不再有新消息
	private sealed class Feed(long roomId, DanmakuLiveFeed feed, IDisposable subscription) : IDanmakuFeed
	{
		public long RoomId { get; } = roomId;

		public long Version => feed.Version;

		public DanmakuFeedStatus Status => feed.Status;

		public IReadOnlyList<LiveDanmakuEntryDto> GetEntries(long afterSequence)
		{
			return feed.GetEntries(afterSequence);
		}

		public void Dispose()
		{
			subscription.Dispose();
		}
	}

	[LoggerMessage(Level = LogLevel.Warning, Message = "Failed to write {Path}")]
	private static partial void LogSubtitleNotWritten(ILogger logger, Exception exception, string path);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Failed to read the video size of {Path}")]
	private static partial void LogResolutionUnreadable(ILogger logger, Exception exception, string path);
}
