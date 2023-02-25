using Serilog.Events;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace BilibiliLiveRecordDownLoader.Views.ValueConverters;

public class ViewBindToStringConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		if (value is LogEventLevel level)
		{
			return level switch
			{
				LogEventLevel.Debug => @"调试",
				LogEventLevel.Error => @"错误",
				LogEventLevel.Fatal => @"致命",
				LogEventLevel.Information => @"信息",
				LogEventLevel.Verbose => @"详细",
				LogEventLevel.Warning => @"警告",
				_ => @"未知"
			};
		}

		return DependencyProperty.UnsetValue;
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
	{
		throw new NotSupportedException();
	}
}
