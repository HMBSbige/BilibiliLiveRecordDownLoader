namespace BilibiliLiveRecordDownLoader.Views.Converters;

public sealed class StatusBrushConverter : IValueConverter
{
	public static StatusBrushConverter Instance { get; } = new();

	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		return value switch
		{
			LiveStatus.Live => StatusBrushes.Positive,
			LiveStatus.Round => StatusBrushes.Warning,
			RecordStatus.Recording => StatusBrushes.Danger,
			RecordStatus.Connecting => StatusBrushes.Warning,
			BackgroundTaskStatus.Completed => StatusBrushes.Positive,
			BackgroundTaskStatus.Failed => StatusBrushes.Danger,
			BackgroundTaskStatus.Running => StatusBrushes.Warning,
			LogLevel.Warning => StatusBrushes.Warning,
			LogLevel.Error or LogLevel.Critical => StatusBrushes.Danger,
			LiveStatus or RecordStatus or BackgroundTaskStatus => StatusBrushes.Neutral,
			_ => null
		};
	}

	public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		throw new NotSupportedException();
	}
}
