using System.Text;
using System.Xml;

namespace BilibiliLiveRecordDownLoader.Application.Danmaku;

internal static class DanmakuXmlText
{
	private const long MaxWholeSeconds = long.MaxValue / TimeSpan.TicksPerSecond;

	// 换行和制表符也写成字符引用，保证每个元素占一行，属性值里的空白读回时也不会被规范化成空格
	// XML 不允许的字符（控制字符、落单的代理项等）丢掉
	public static void AppendEscaped(StringBuilder builder, string value)
	{
		for (int i = 0; i < value.Length; ++i)
		{
			char c = value[i];
			string? reference = c switch
			{
				'&' => @"&amp;",
				'<' => @"&lt;",
				'>' => @"&gt;",
				'"' => @"&quot;",
				'\t' => @"&#x9;",
				'\n' => @"&#xA;",
				'\r' => @"&#xD;",
				_ => null
			};

			if (reference is not null)
			{
				builder.Append(reference);
			}
			else if (XmlConvert.IsXmlChar(c))
			{
				builder.Append(c);
			}
			else if (i + 1 < value.Length && XmlConvert.IsXmlSurrogatePair(value[i + 1], c))
			{
				builder.Append(c).Append(value[++i]);
			}
		}
	}

	// 取整到毫秒用截断，写回读出的值时不会因为舍入漂移
	public static string FormatSeconds(TimeSpan value)
	{
		long milliseconds = value.Ticks / TimeSpan.TicksPerMillisecond;
		string sign = milliseconds < 0 ? @"-" : string.Empty;
		milliseconds = Math.Abs(milliseconds);
		return string.Create(CultureInfo.InvariantCulture, $@"{sign}{milliseconds / 1000}.{milliseconds % 1000:000}");
	}

	public static string FormatTimestamp(DateTimeOffset? value)
	{
		return value?.ToString(@"yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture) ?? string.Empty;
	}

	// 用 decimal 解析，12.345 不会变成 12.344999…，改写偏移时不会丢一毫秒
	// 上限按 tick 判断：TimeSpan.MaxValue.TotalSeconds 是 double，转成 decimal 时按 15 位有效数字舍入，和真正的上限对不上
	public static bool TryParseSeconds(string? value, out TimeSpan result)
	{
		// 先粗略限定范围，乘以每秒的 tick 数时 decimal 才不会溢出
		if (decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal seconds) && decimal.Abs(seconds) <= MaxWholeSeconds + 1 && decimal.Truncate(seconds * TimeSpan.TicksPerSecond) is var ticks and >= long.MinValue and <= long.MaxValue)
		{
			result = new TimeSpan((long)ticks);
			return true;
		}

		result = default;
		return false;
	}

	public static bool TryParseTimestamp(string? value, out DateTimeOffset result)
	{
		return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
	}

	public static long ParseInt64(string? value, long fallback = 0)
	{
		return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long result) ? result : fallback;
	}

	public static int ParseInt32(string? value, int fallback = 0)
	{
		return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) ? result : fallback;
	}

	public static string Format(long value)
	{
		return value.ToString(CultureInfo.InvariantCulture);
	}
}
