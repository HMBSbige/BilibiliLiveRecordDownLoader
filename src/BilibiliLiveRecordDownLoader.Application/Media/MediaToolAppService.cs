namespace BilibiliLiveRecordDownLoader.Application.Media;

internal sealed partial class MediaToolAppService(FFmpegProvider ffmpegProvider, BackgroundTaskAppService backgroundTasks, ILogger<MediaToolAppService> logger) : IMediaToolAppService
{
	private const string WorkDirectoryPrefix = ".remux-";

	// .mp4 只有本程序录的 fMP4 需要转换，由 GetMp4ConversionPath 识别
	private static readonly HashSet<string> RemuxableExtensions = [with(StringComparer.OrdinalIgnoreCase), ".flv", ".ts", ".mkv", ".m4s", ".mp4"];

	// 目录的修改时间只在增删条目时更新，阈值要远长于一次转封装
	private static readonly TimeSpan StaleWorkDirectoryAge = TimeSpan.FromDays(1);

	public async Task<string?> GetFFmpegVersionAsync(CancellationToken cancellationToken = default)
	{
		FFmpegRunner? runner = await ffmpegProvider.GetRunnerAsync(cancellationToken);

		return runner is null ? null : await runner.GetVersionAsync(cancellationToken);
	}

	public async Task<Task<BackgroundTaskSnapshot>> QueueRemuxAsync(RemuxInput input, CancellationToken cancellationToken = default)
	{
		FFmpegRunner runner = await ffmpegProvider.GetRequiredRunnerAsync(cancellationToken);
		EnsureValidPaths(input.Source, input.Destination);

		string fileName = Path.GetFileName(input.Source);
		bool repair = input.Mode is RemuxMode.RepairFlv && Path.GetExtension(input.Source).Equals(".flv", StringComparison.OrdinalIgnoreCase);
		string title = repair ? AppLanguage.Current.TaskRepairRemux.ToString(fileName) : AppLanguage.Current.TaskRemux.ToString(fileName);

		return backgroundTasks.Enqueue
		(
			title,
			FileOperations.GetQueueKey(input.Destination),
			async (context, token) =>
			{
				await WriteDestinationAsync
				(
					input.Destination,
					output => input.Mode switch
					{
						RemuxMode.RepairFlv when repair => RepairAndRemuxAsync(runner, input.Source, output, context, token),
						RemuxMode.WithChapters { Path: var chapters } => runner.RunAsync(FFmpegArguments.RemuxWithChapters(input.Source, chapters, output), context.CreateFFmpegProgress(), token),
						RemuxMode.Plain or RemuxMode.RepairFlv => runner.RunAsync(FFmpegArguments.Remux(input.Source, output), context.CreateFFmpegProgress(), token)
					}
				);

				if (input.DeleteSource)
				{
					FileOperations.TryDelete(input.Source, logger);
				}
			},
			input.Source,
			input.Destination
		);
	}

	public async Task QueueCutAsync(CutInput input, CancellationToken cancellationToken = default)
	{
		if (input.Start < TimeSpan.Zero || input.End <= input.Start)
		{
			throw new ArgumentException(AppLanguage.Current.InvalidTimeRange);
		}

		FFmpegRunner runner = await ffmpegProvider.GetRequiredRunnerAsync(cancellationToken);
		EnsureValidPaths(input.Source, input.Destination);

		_ = backgroundTasks.Enqueue
		(
			AppLanguage.Current.TaskCut.ToString(Path.GetFileName(input.Source)),
			FileOperations.GetQueueKey(input.Destination),
			(context, token) => WriteDestinationAsync
			(
				input.Destination,
				output => runner.RunAsync(FFmpegArguments.Cut(input.Source, output, input.Start, input.End), context.CreateFFmpegProgress(input.End - input.Start), token)
			),
			input.Source,
			input.Destination
		);
	}

	public string? SuggestRemuxDestination(string source)
	{
		if (!RemuxableExtensions.Contains(Path.GetExtension(source)) || RecordingFileLayout.GetMp4ConversionPath(source) is not { } destination)
		{
			return null;
		}

		return FirstAvailable(Numbered(destination).Prepend(destination));
	}

	public string? SuggestCutDestination(string source)
	{
		return string.IsNullOrWhiteSpace(source) ? null : FirstAvailable(Numbered(source));
	}

	// 排队的任务还没写出文件，建议的输出也要跳过它们
	private string FirstAvailable(IEnumerable<string> candidates)
	{
		IEnumerable<string> pending = backgroundTasks.GetTasks().Where(static task => task.IsActive).Select(static task => task.TargetPath).OfType<string>();
		HashSet<string> reserved = [with(StringComparer.FromComparison(FileOperations.PathComparison)), .. pending.Select(Path.GetFullPath)];

		return candidates.First(candidate => !File.Exists(candidate) && !reserved.Contains(Path.GetFullPath(candidate)));
	}

	private static IEnumerable<string> Numbered(string path)
	{
		string withoutExtension = Path.ChangeExtension(path, null);
		string extension = Path.GetExtension(path);

		return Enumerable.InfiniteSequence(1, 1).Select(index => string.Create(CultureInfo.InvariantCulture, $"{withoutExtension}_{index}{extension}"));
	}

	// 拆出的裸流放在输出旁的子目录，文件名才不会和输出撞上；output 在工作目录里，子目录随它一起删除
	private static async Task RepairAndRemuxAsync(FFmpegRunner runner, string source, string output, BackgroundTaskContext context, CancellationToken cancellationToken)
	{
		FlvExtractResult extracted = await ExtractFlvAsync(source, output + ".streams", context.CreateProgress(weight: 0.5), cancellationToken);

		IReadOnlyList<string> arguments = extracted switch
		{
			{ VideoPath: { } video, AudioPath: { } audio } => FFmpegArguments.Merge(video, audio, output, extracted.VideoFrameRate),
			{ VideoPath: { } video } => FFmpegArguments.Remux(video, output, extracted.VideoFrameRate),
			{ AudioPath: { } audio } => FFmpegArguments.Remux(audio, output),
			{ VideoPath: null, AudioPath: null } => throw new InvalidDataException(AppLanguage.Current.FlvNoSupportedTracks.ToString(Path.GetFileName(source)))
		};

		await runner.RunAsync(arguments, context.CreateFFmpegProgress(start: 0.5, weight: 0.5), cancellationToken);
	}

	// MediaToolkit 的异常文案是英文，任务列表直接显示异常消息
	private static async Task<FlvExtractResult> ExtractFlvAsync(string source, string workDirectory, IProgress<double> progress, CancellationToken cancellationToken)
	{
		try
		{
			return await FlvExtractor.ExtractAsync(source, workDirectory, progress, cancellationToken);
		}
		catch (InvalidDataException exception)
		{
			throw new InvalidDataException(AppLanguage.Current.NotFlvFile.ToString(Path.GetFileName(source)), exception);
		}
		catch (NotSupportedException exception)
		{
			throw new NotSupportedException(AppLanguage.Current.FlvRepairUnsupportedCodec.ToString(Path.GetFileName(source)), exception);
		}
	}

	// 写完再改名：别处只按目标路径是否存在来判断，强杀时目标路径上不能留下没写完文件尾的视频
	// 工作目录里沿用目标的文件名，FFmpeg 按扩展名选封装格式
	// FFmpeg 的最后几行输出是界面上唯一的诊断信息，换成本地化文案时要保留
	private async Task WriteDestinationAsync(string destination, Func<string, Task> write)
	{
		string workDirectory = CreateWorkDirectory(destination);

		try
		{
			string output = Path.Combine(workDirectory, Path.GetFileName(destination));
			await write(output);
			File.Move(output, destination, true);
		}
		catch (FFmpegException exception)
		{
			throw new InvalidOperationException(AppLanguage.Current.FFmpegFailed.ToString(exception.ExitCode, exception.LastMessage ?? string.Empty), exception);
		}
		finally
		{
			FileOperations.TryDeleteDirectory(workDirectory, logger);
		}
	}

	// 放在目标所在目录，改名不跨卷；名字带点只在 Unix 上算隐藏，Windows 上要另设属性
	private string CreateWorkDirectory(string destination)
	{
		string parent = Path.GetDirectoryName(Path.GetFullPath(destination)) ?? Path.GetTempPath();
		DeleteStaleWorkDirectories(parent);

		DirectoryInfo directory = Directory.CreateDirectory(Path.Combine(parent, $"{WorkDirectoryPrefix}{Guid.NewGuid():N}"));

		if (OperatingSystem.IsWindows())
		{
			directory.Attributes |= FileAttributes.Hidden;
		}

		return directory.FullName;
	}

	// 强杀或断电时 finally 不会执行；比较的是文件系统记下的时间，所以用系统时钟
	private void DeleteStaleWorkDirectories(string parent)
	{
		DateTime threshold = DateTime.UtcNow - StaleWorkDirectoryAge;

		try
		{
			foreach (DirectoryInfo stale in new DirectoryInfo(parent).EnumerateDirectories($"{WorkDirectoryPrefix}*").Where(directory => directory.LastWriteTimeUtc < threshold))
			{
				FileOperations.TryDeleteDirectory(stale.FullName, logger);
			}
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			LogStaleWorkDirectoriesUnavailable(logger, exception, parent);
		}
	}

	private static void EnsureValidPaths(string source, string destination)
	{
		if (!File.Exists(source))
		{
			throw new FileNotFoundException(AppLanguage.Current.FileNotFound.ToString(source), source);
		}

		if (FileOperations.IsSamePath(source, destination))
		{
			throw new ArgumentException(AppLanguage.Current.SameInputAndOutput);
		}
	}

	[LoggerMessage(Level = LogLevel.Warning, Message = "Failed to look for stale remux work directories in {Path}")]
	private static partial void LogStaleWorkDirectoriesUnavailable(ILogger logger, Exception exception, string path);
}
