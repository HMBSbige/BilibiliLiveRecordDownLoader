namespace BilibiliLiveRecordDownLoader.Application.Tasks;

internal sealed class BackgroundTaskContext(Action<double?, double?> report, TimeProvider timeProvider)
{
	public void Report(double? progress)
	{
		Report(progress, null);
	}

	public IProgress<double> CreateProgress(double start = 0, double weight = 1)
	{
		return new ScaledProgress(this, start, weight);
	}

	public IProgress<FFmpegProgress> CreateFFmpegProgress(TimeSpan? expectedDuration = null, double start = 0, double weight = 1)
	{
		return new FFmpegProgressReporter(this, timeProvider, expectedDuration, start, weight);
	}

	private void Report(double? progress, double? speed)
	{
		report(progress is { } value ? Math.Clamp(value, 0, 1) : null, speed);
	}

	private sealed class ScaledProgress(BackgroundTaskContext context, double start, double weight) : IProgress<double>
	{
		public void Report(double value)
		{
			context.Report(start + value * weight);
		}
	}

	// 速度与 FFmpeg 的算法一致
	private sealed class FFmpegProgressReporter(BackgroundTaskContext context, TimeProvider timeProvider, TimeSpan? expectedDuration, double start, double weight) : IProgress<FFmpegProgress>
	{
		private readonly long _startedAt = timeProvider.GetTimestamp();
		private TimeSpan? _duration = expectedDuration;

		public void Report(FFmpegProgress value)
		{
			_duration ??= value.Duration;

			double? progress = (value.Position, _duration) switch
			{
				({ } position, { TotalSeconds: > 0 } duration) => start + Math.Min(position / duration, 1) * weight,
				_ => null
			};

			double? speed = (value.Position, timeProvider.GetElapsedTime(_startedAt)) switch
			{
				({ TotalSeconds: > 0 } position, { TotalSeconds: > 0 } elapsed) => position / elapsed,
				_ => null
			};

			context.Report(progress, speed);
		}
	}
}
