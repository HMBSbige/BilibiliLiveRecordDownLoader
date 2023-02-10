using BilibiliLiveRecordDownLoader.Enums;
using BilibiliLiveRecordDownLoader.Utils;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace BilibiliLiveRecordDownLoader.Views.ValueConverters;

public class QnToStringConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		if (value is Qn qn)
		{
			return qn switch
			{
				Qn._4K => Constants.Qn20000,
				Qn.原画 => Constants.Qn10000,
				Qn.蓝光杜比 => Constants.Qn401,
				Qn.蓝光 => Constants.Qn400,
				Qn.超清 => Constants.Qn250,
				Qn.高清 => Constants.Qn150,
				Qn.流畅 => Constants.Qn80,
				_ => $@"{qn}"
			};
		}

		return DependencyProperty.UnsetValue;
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
	{
		if (value is string str)
		{
			if (long.TryParse(str, out var l))
			{
				return (Qn)l;
			}
			return str switch
			{
				Constants.Qn20000 => Qn._4K,
				Constants.Qn10000 => Qn.原画,
				Constants.Qn401 => Qn.蓝光杜比,
				Constants.Qn400 => Qn.蓝光,
				Constants.Qn250 => Qn.超清,
				Constants.Qn150 => Qn.高清,
				Constants.Qn80 => Qn.流畅,
				_ => Qn.原画
			};
		}
		return DependencyProperty.UnsetValue;
	}
}
