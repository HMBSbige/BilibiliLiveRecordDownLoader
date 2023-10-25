using BilibiliLiveRecordDownLoader.Enums;
using System.Globalization;
using System.Windows.Data;

namespace BilibiliLiveRecordDownLoader.Views.ValueConverters;

public class RecorderTypeToIntConverter : IValueConverter
{
	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		if (value is RecorderType type && Enum.IsDefined(type) && int.TryParse(parameter?.ToString(), out int i))
		{
			return i == (int)type;
		}

		return parameter is @"0";
	}

	public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		if (int.TryParse(parameter?.ToString(), out int i))
		{
			RecorderType type = (RecorderType)i;
			if (Enum.IsDefined(type))
			{
				return type;
			}
		}
		return RecorderType.Default;
	}
}
