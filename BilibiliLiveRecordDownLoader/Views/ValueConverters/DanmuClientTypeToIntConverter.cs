using BilibiliApi.Enums;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace BilibiliLiveRecordDownLoader.Views.ValueConverters;

public class DanmuClientTypeToIntConverter : IValueConverter
{
	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		if (value is DanmuClientType type && Enum.IsDefined(type))
		{
			int i = (int)type;
			return i;
		}
		return DependencyProperty.UnsetValue;
	}

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		if (value is int i)
		{
			DanmuClientType type = (DanmuClientType)i;
			if (Enum.IsDefined(type))
			{
				return type;
			}
		}
		return DependencyProperty.UnsetValue;
	}
}
