using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace BilibiliLiveRecordDownLoader.Views.ValueConverters;

public class NullableBoolToIntConverter : IValueConverter
{
	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		return value switch
		{
			null => 0,
			true => 1,
			false => 2,
			_ => DependencyProperty.UnsetValue
		};
	}

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		return value switch
		{
			0 => null,
			1 => true,
			2 => false,
			_ => DependencyProperty.UnsetValue
		};
	}
}
