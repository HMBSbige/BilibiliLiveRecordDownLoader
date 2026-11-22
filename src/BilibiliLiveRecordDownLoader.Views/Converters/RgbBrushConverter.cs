namespace BilibiliLiveRecordDownLoader.Views.Converters;

// 没有颜色时不设值，沿用样式里的默认画刷
public sealed class RgbBrushConverter : IValueConverter
{
	public static RgbBrushConverter Instance { get; } = new();

	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		return value is int rgb ? new ImmutableSolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb)) : AvaloniaProperty.UnsetValue;
	}

	public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		throw new NotSupportedException();
	}
}
