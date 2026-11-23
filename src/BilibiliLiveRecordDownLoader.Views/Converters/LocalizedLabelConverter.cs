namespace BilibiliLiveRecordDownLoader.Views.Converters;

// 只用第一个值；其余的绑定只用于在切换语言时触发刷新
public sealed class LocalizedLabelConverter : IMultiValueConverter
{
	public static LocalizedLabelConverter Instance { get; } = new();

	public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
	{
		return Labels.Of(values[0]);
	}
}
