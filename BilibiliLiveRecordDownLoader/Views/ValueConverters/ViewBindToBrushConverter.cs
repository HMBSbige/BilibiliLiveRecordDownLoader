using BilibiliApi.Enums;
using BilibiliLiveRecordDownLoader.Enums;
using BilibiliLiveRecordDownLoader.Utils;
using Serilog.Events;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace BilibiliLiveRecordDownLoader.Views.ValueConverters;

public class ViewBindToBrushConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		if (value is LiveStatus status)
		{
			return status switch
			{
				LiveStatus.未知 => Constants.RedBrush,
				LiveStatus.闲置 => Constants.RedBrush,
				LiveStatus.直播 => Constants.NormalBlueBrush,
				LiveStatus.轮播 => Constants.YellowBrush,
				_ => DependencyProperty.UnsetValue
			};
		}

		if (value is RecordStatus recordStatus)
		{
			return recordStatus switch
			{
				RecordStatus.未录制 => Constants.RedBrush,
				RecordStatus.启动中 => Constants.YellowBrush,
				RecordStatus.录制中 => Constants.NormalBlueBrush,
				_ => DependencyProperty.UnsetValue
			};
		}

		if (value is LogEventLevel level)
		{
			return level switch
			{
				LogEventLevel.Debug => Constants.GreenBrush,
				LogEventLevel.Error => Constants.RedBrush,
				LogEventLevel.Fatal => Constants.RedBrush,
				LogEventLevel.Information => Constants.NormalBlueBrush,
				LogEventLevel.Verbose => Constants.GreenBrush,
				LogEventLevel.Warning => Constants.YellowBrush,
				_ => DependencyProperty.UnsetValue
			};
		}

		return DependencyProperty.UnsetValue;
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
	{
		throw new NotSupportedException();
	}
}
