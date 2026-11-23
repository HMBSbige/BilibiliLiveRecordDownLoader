using Avalonia.Media.Imaging;
using QRCoder;

namespace BilibiliLiveRecordDownLoader.Views.Converters;

public sealed class QrCodeImageConverter : IValueConverter
{
	public static QrCodeImageConverter Instance { get; } = new();

	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		if (value is not string { Length: > 0 } content)
		{
			return null;
		}

		using MemoryStream stream = new(PngByteQRCodeHelper.GetQRCode(content, QRCodeGenerator.ECCLevel.M, 8));
		return new Bitmap(stream);
	}

	public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		throw new NotSupportedException();
	}
}
