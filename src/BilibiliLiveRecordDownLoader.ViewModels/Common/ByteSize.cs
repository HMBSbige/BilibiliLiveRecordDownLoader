namespace BilibiliLiveRecordDownLoader.ViewModels.Common;

public static class ByteSize
{
	private static readonly string[] Units = ["B", "KiB", "MiB", "GiB", "TiB", "PiB"];

	public static string Format(double bytes)
	{
		int unit = 0;

		while (bytes >= 1024 && unit < Units.Length - 1)
		{
			bytes /= 1024;
			++unit;
		}

		return string.Create(CultureInfo.CurrentCulture, $"{bytes:0.##} {Units[unit]}");
	}
}
