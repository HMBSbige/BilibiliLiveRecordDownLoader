namespace BilibiliLiveRecordDownLoader.Views.Converters;

public sealed class LocalizedLabelConverter : IMultiValueConverter
{
	public static LocalizedLabelConverter Instance { get; } = new();

	public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
	{
		return Labels.Of(values[0]);
	}
}
