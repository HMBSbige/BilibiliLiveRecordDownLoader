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

	public static ColorText Color(int rgb)
	{
		return new ColorText(rgb);
	}

	public static TimeText Time(TimeSpan value)
	{
		return new TimeText(value);
	}

	// ASS 的颜色是 BGR
	public readonly struct ColorText(int rgb) : ISpanFormattable
	{
		public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
		{
			return destination.TryWrite(CultureInfo.InvariantCulture, $@"{rgb & 0xFF:X2}{rgb >> 8 & 0xFF:X2}{rgb >> 16 & 0xFF:X2}", out charsWritten);
		}

		public string ToString(string? format, IFormatProvider? formatProvider)
		{
			return ToString();
		}

		public override string ToString()
		{
			return string.Create(CultureInfo.InvariantCulture, $"{this}");
		}
	}

	public readonly struct TimeText(TimeSpan value) : ISpanFormattable
	{
		public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
		{
			long centiseconds = value.Ticks / (TimeSpan.TicksPerMillisecond * 10);
			return destination.TryWrite(CultureInfo.InvariantCulture, $@"{centiseconds / 360000}:{centiseconds / 6000 % 60:00}:{centiseconds / 100 % 60:00}.{centiseconds % 100:00}", out charsWritten);
		}

		public string ToString(string? format, IFormatProvider? formatProvider)
		{
			return ToString();
		}

		public override string ToString()
		{
			return string.Create(CultureInfo.InvariantCulture, $"{this}");
		}
	}
}
