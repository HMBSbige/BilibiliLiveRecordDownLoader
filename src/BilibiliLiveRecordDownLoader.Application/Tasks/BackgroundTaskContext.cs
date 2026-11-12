namespace BilibiliLiveRecordDownLoader.Application.Tasks;

internal sealed class BackgroundTaskContext(Action<double?, string?> report)
{
	public void Report(double? progress, string? message = null)
	{
		report(progress is { } value ? Math.Clamp(value, 0, 1) : null, message);
	}

	public IProgress<double> CreateProgress(double start = 0, double weight = 1)
	{
		return new ScaledProgress(this, start, weight);
	}

	public IProgress<FFmpegProgress> CreateFFmpegProgress(TimeSpan? expectedDuration = null, double start = 0, double weight = 1)
	{
		return new FFmpegProgressReporter(this, expectedDuration, start, weight);
	}

	private sealed class ScaledProgress(BackgroundTaskContext context, double start, double weight) : IProgress<double>
	{
		public void Report(double value)
		{
			context.Report(start + value * weight);
		}
	}

	private sealed class FFmpegProgressReporter(BackgroundTaskContext context, TimeSpan? expectedDuration, double start, double weight) : IProgress<FFmpegProgress>
	{
		private TimeSpan? _duration = expectedDuration;

		public void Report(FFmpegProgress value)
		{
			_duration ??= value.Duration;

			double? progress = (value.Position, _duration) switch
			{
				({ } position, { TotalSeconds: > 0 } duration) => start + Math.Min(position / duration, 1) * weight,
				_ => null
			};

			context.Report(progress, value.Message);
		}
	}
}
