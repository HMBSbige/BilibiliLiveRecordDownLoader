using System.Text;
using System.Xml;

namespace BilibiliLiveRecordDownLoader.Application.Danmaku;

internal static class DanmakuXmlText
{
	// 换行和制表符也写成字符引用，保证每个元素占一行，属性值里的空白读回时也不会被规范化成空格
	public static void AppendEscaped(StringBuilder builder, string value)
	{
		for (int i = 0; i < value.Length; ++i)
		{
			char c = value[i];

			switch (c)
			{
				case '&':
					builder.Append(@"&amp;");
					break;
				case '<':
					builder.Append(@"&lt;");
					break;
				case '>':
					builder.Append(@"&gt;");
					break;
				case '"':
					builder.Append(@"&quot;");
					break;
				case '\t':
					builder.Append(@"&#x9;");
					break;
				case '\n':
					builder.Append(@"&#xA;");
					break;
				case '\r':
					builder.Append(@"&#xD;");
					break;
				default:
				{
					if (XmlConvert.IsXmlChar(c))
					{
						builder.Append(c);
					}
					else if (i + 1 < value.Length && XmlConvert.IsXmlSurrogatePair(value[i + 1], c))
					{
						builder.Append(c).Append(value[++i]);
					}

					break;
				}
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
	public static bool TryParseSeconds(string? value, out TimeSpan result)
	{
		if (decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal seconds) && Math.Abs(seconds) < (decimal)TimeSpan.MaxValue.TotalSeconds)
		{
			result = new TimeSpan((long)(seconds * TimeSpan.TicksPerSecond));
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
