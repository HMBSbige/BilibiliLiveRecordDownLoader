using BilibiliLiveRecordDownLoader.Enums;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace BilibiliLiveRecordDownLoader.Views.ValueConverters
{
	public class QnToStringConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value is long l)
			{
				var q = (Qn)l;
				if (Enum.IsDefined(q))
				{
					value = q;
				}
				else
				{
					return $@"{l}";
				}
			}

			if (value is Qn qn)
			{
				return qn switch
				{
					Qn._4K => @"4K",
					Qn.蓝光杜比 => @"蓝光(杜比)",
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
					@"原画" => Qn.原画,
					@"4K" => Qn._4K,
					@"蓝光(杜比)" => Qn.蓝光杜比,
					@"蓝光" => Qn.蓝光,
					@"超清" => Qn.超清,
					@"高清" => Qn.高清,
					@"流畅" => Qn.流畅,
					_ => Qn.原画
				};
			}
			return DependencyProperty.UnsetValue;
		}
	}
}
