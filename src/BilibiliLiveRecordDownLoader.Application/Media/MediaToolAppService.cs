namespace BilibiliLiveRecordDownLoader.Application.Media;

internal sealed class MediaToolAppService(FFmpegProvider ffmpegProvider, BackgroundTaskAppService backgroundTasks, ILogger<MediaToolAppService> logger) : IMediaToolAppService
{
	public async Task<string?> GetFFmpegVersionAsync(CancellationToken cancellationToken = default)
	{
		FFmpegRunner? runner = await ffmpegProvider.GetRunnerAsync(cancellationToken);

		return runner is null ? null : await runner.GetVersionAsync(cancellationToken);
	}

	public async Task QueueRemuxAsync(RemuxInput input, CancellationToken cancellationToken = default)
	{
		FFmpegRunner runner = await ffmpegProvider.GetRequiredRunnerAsync(cancellationToken);
		EnsureValidPaths(input.Source, input.Destination);

		string fileName = Path.GetFileName(input.Source);
		bool repair = input.RepairFlv && Path.GetExtension(input.Source).Equals(".flv", StringComparison.OrdinalIgnoreCase);
		string title = repair ? AppLanguage.Current.TaskRepairRemux.ToString(fileName) : AppLanguage.Current.TaskRemux.ToString(fileName);

		backgroundTasks.Enqueue
		(
			title,
			FileOperations.GetQueueKey(input.Destination),
			async (context, token) =>
			{
				await WriteDestinationAsync
				(
					input.Destination,
					() => repair
						? RepairAndRemuxAsync(runner, input, context, token)
						: runner.RunAsync(FFmpegArguments.Remux(input.Source, input.Destination), context.CreateFFmpegProgress(), token)
				);

				if (input.DeleteSource)
				{
					FileOperations.TryDelete(input.Source, logger);
				}
			}
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

		backgroundTasks.Enqueue
		(
			AppLanguage.Current.TaskCut.ToString(Path.GetFileName(input.Source)),
			FileOperations.GetQueueKey(input.Destination),
			(context, token) => WriteDestinationAsync
			(
				input.Destination,
				() => runner.RunAsync(FFmpegArguments.Cut(input.Source, input.Destination, input.Start, input.End), context.CreateFFmpegProgress(input.End - input.Start), token)
			)
		);
	}

	// 时间戳错乱的 FLV 先拆出裸流再合并，由 FFmpeg 重新生成时间戳
	private async Task RepairAndRemuxAsync(FFmpegRunner runner, RemuxInput input, BackgroundTaskContext context, CancellationToken cancellationToken)
	{
		string workDirectory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(input.Destination)) ?? Path.GetTempPath(), $".{Guid.NewGuid():N}");
		Directory.CreateDirectory(workDirectory);

		try
		{
			FlvExtractResult extracted = await FlvExtractor.ExtractAsync(input.Source, workDirectory, context.CreateProgress(weight: 0.5), cancellationToken);

			IReadOnlyList<string> arguments = extracted switch
			{
				{ VideoPath: { } video, AudioPath: { } audio } => FFmpegArguments.Merge(video, audio, input.Destination, extracted.VideoFrameRate),
				{ VideoPath: { } video } => FFmpegArguments.Remux(video, input.Destination, extracted.VideoFrameRate),
				{ AudioPath: { } audio } => FFmpegArguments.Remux(audio, input.Destination),
				_ => throw new InvalidDataException(AppLanguage.Current.FileNotFound.ToString(input.Source))
			};

			await runner.RunAsync(arguments, context.CreateFFmpegProgress(start: 0.5, weight: 0.5), cancellationToken);
		}
		finally
		{
			FileOperations.TryDeleteDirectory(workDirectory, logger);
		}
	}

	// 失败或取消时删掉写了一半的输出，避免留下无法播放的文件
	private async Task WriteDestinationAsync(string destination, Func<Task> write)
	{
		try
		{
			await write();
		}
		catch
		{
			FileOperations.TryDelete(destination, logger);
			throw;
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
}
