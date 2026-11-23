using System.Buffers;
using System.Text;

namespace BilibiliLiveRecordDownLoader.Application.Danmaku;

internal static class DanmakuAssText
{
	private static readonly SearchValues<char> Special = SearchValues.Create([.. @"{}\", .. Enumerable.Range(0, 0xA0).Select(static c => (char)c).Where(char.IsControl)]);

	// ASS 里 { } 是样式代码，\N、\n、\h 是换行和硬空格，换成全角字符才能原样显示
	public static string Escape(string text)
	{
		if (!text.AsSpan().ContainsAny(Special))
		{
			return text.Trim();
		}

		StringBuilder builder = new(text.Length);

		foreach (char c in text)
		{
			char escaped = c switch
			{
				'{' => '｛',
				'}' => '｝',
				'\\' => '＼',
				'\n' or '\r' or '\t' => ' ',
				_ => c
			};

			if (!char.IsControl(escaped))
			{
				builder.Append(escaped);
			}
		}

		return builder.ToString().Trim();
	}

	public static bool IsWide(int codePoint)
	{
		return codePoint is >= 0x1100 and <= 0x115F
			or >= 0x2E80 and <= 0xA4CF
			or >= 0xAC00 and <= 0xD7A3
			or >= 0xF900 and <= 0xFAFF
			or >= 0xFE30 and <= 0xFE4F
			or >= 0xFF00 and <= 0xFF60
			or >= 0xFFE0 and <= 0xFFE6
			or >= 0x1F000;
	}

	// ASS 的颜色是 BGR
	public static string Color(int rgb)
	{
		return string.Create(CultureInfo.InvariantCulture, $@"{rgb & 0xFF:X2}{rgb >> 8 & 0xFF:X2}{rgb >> 16 & 0xFF:X2}");
	}

	public static string Time(TimeSpan value)
	{
		long centiseconds = value.Ticks / (TimeSpan.TicksPerMillisecond * 10);
		return string.Create(CultureInfo.InvariantCulture, $@"{centiseconds / 360000}:{centiseconds / 6000 % 60:00}:{centiseconds / 100 % 60:00}.{centiseconds % 100:00}");
	}
}
