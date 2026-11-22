namespace BilibiliLiveRecordDownLoader.Application.Danmaku;

internal sealed partial class DanmakuAppService(RoomAppService rooms, IMediaToolAppService mediaTools, IAppConfigManager configManager, ILogger<DanmakuAppService> logger) : IDanmakuAppService
{
	// 密度曲线最多这么多格，更长的文件按比例放宽每格的时长
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

	public async Task<DanmakuFileDto> OpenFileAsync(string path, CancellationToken cancellationToken = default)
	{
		DanmakuFile file = await DanmakuXmlReader.ReadFileAsync(path, cancellationToken);

		if (file is { Records.Count: 0, IsComplete: false })
		{
			throw new InvalidDataException(AppLanguage.Current.InvalidDanmakuFile.ToString(path));
		}

		DanmakuFileSourceDto source = file.Source switch
		{
			DanmakuFileSource.Native native => new DanmakuFileSourceDto.Native(DanmakuConverter.ToDto(native.Header)),
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

	public async Task<DanmakuOffsetResult> ChangeOffsetAsync(string path, TimeSpan offset, CancellationToken cancellationToken = default)
	{
		// Linux 上替换正在写的文件不会失败，写入者会接着写进已经删除的旧文件
		if (rooms.IsRecordingDanmakuFile(path))
		{
			throw new IOException(AppLanguage.Current.DanmakuFileInUse.ToString(path));
		}

		DanmakuFileHeader header;

		try
		{
			header = await DanmakuOffsetRewriter.RewriteAsync(path, DanmakuDefaults.ClampOffset(offset), cancellationToken);
		}
		catch (InvalidDataException exception)
		{
			throw new InvalidDataException(AppLanguage.Current.DanmakuFileNotRewritable, exception);
		}

		DanmakuFileHeaderDto dto = DanmakuConverter.ToDto(header);

		if (FindVideo(path) is not { } video)
		{
			return new DanmakuOffsetResult.Updated(dto, null);
		}

		string subtitle = RecordingFileLayout.GetStem(path) + RecordingFileLayout.SubtitleExtension;
		DanmakuFile file = await DanmakuXmlReader.ReadFileAsync(path, cancellationToken);
		VideoResolution? resolution = await ReadResolutionAsync(video, cancellationToken);
		DanmakuAssLabels? labels = (await configManager.GetAsync(cancellationToken)).ShowPaidMessagesInSubtitle ? RecordingPostProcessor.SubtitleLabels : null;

		try
		{
			await DanmakuAssGenerator.WriteFileAsync(subtitle, file.Records, resolution, labels, cancellationToken);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			// 播放器打开着字幕时写不进去；弹幕文件已经改好，关掉播放器后再改一次就能补上
			LogSubtitleInUse(logger, exception, subtitle);
			return new DanmakuOffsetResult.SubtitleInUse(dto, subtitle, exception.Message);
		}

		return new DanmakuOffsetResult.Updated(dto, subtitle);
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
		string destination = GetHighlightPath(video, start, File.Exists);
		await mediaTools.QueueCutAsync(new CutInput(video, destination, start, end), cancellationToken);
		return destination;
	}

	// 弹幕的时间已经含偏移，和画面对齐，直接用作视频时间。片段名是视频主干加上 .highlight-时分秒，
	// 录制文件的主干不含这样的后缀，按主干找视频、判断主干是否占用都不会把它当成录像
	internal static string GetHighlightPath(string video, TimeSpan start, Func<string, bool> fileExists)
	{
		string stem = RecordingFileLayout.GetStem(video);
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

			if (!fileExists(path))
			{
				return path;
			}
		}
	}

	internal static string? FindVideo(string danmakuPath)
	{
		return RecordingFileLayout.GetVideoPaths(RecordingFileLayout.GetStem(danmakuPath)).FirstOrDefault(File.Exists);
	}

	internal static DanmakuDensityDto ComputeDensity(IReadOnlyList<DanmakuRecord> records)
	{
		DanmakuRecord.Comment[] comments = [.. records.OfType<DanmakuRecord.Comment>()];

		if (comments.Length is 0)
		{
			return new DanmakuDensityDto(MinDensityBinWidth, []);
		}

		TimeSpan last = comments.Max(static comment => comment.Time);

		// 按整秒放宽，曲线的刻度好读
		TimeSpan width = TimeSpan.FromSeconds(Math.Max(MinDensityBinWidth.TotalSeconds, Math.Floor(last.TotalSeconds / MaxDensityBins) + 1));

		int[] counts = new int[(int)(last.Ticks / width.Ticks) + 1];

		foreach (DanmakuRecord.Comment comment in comments)
		{
			++counts[(int)(Math.Max(comment.Time.Ticks, 0) / width.Ticks)];
		}

		return new DanmakuDensityDto(width, counts);
	}

	// 读不到时按 1920×1080 生成，字幕照样能用，只是字号可能和画面不成比例
	private async Task<VideoResolution?> ReadResolutionAsync(string video, CancellationToken cancellationToken)
	{
		try
		{
			return await VideoResolutionProbe.ReadAsync(video, cancellationToken);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			LogResolutionUnreadable(logger, exception, video);
			return null;
		}
	}

	// 退订之后不再取消第二次；房间移除之后订阅仍然可用，只是不再有新消息
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

	[LoggerMessage(Level = LogLevel.Warning, Message = "Failed to write {Path}, it may be open in a player")]
	private static partial void LogSubtitleInUse(ILogger logger, Exception exception, string path);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Failed to read the video size of {Path}, using 1920×1080")]
	private static partial void LogResolutionUnreadable(ILogger logger, Exception exception, string path);
}
