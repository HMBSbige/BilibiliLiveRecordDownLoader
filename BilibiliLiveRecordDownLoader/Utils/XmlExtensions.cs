using System.Text;
using System.Xml;

namespace BilibiliLiveRecordDownLoader.Utils;

public static class XmlExtensions
{
	public static string EscapeXmlChars(this string? str)
	{
		if (string.IsNullOrEmpty(str))
		{
			return string.Empty;
		}

		var sb = new StringBuilder(str.Length);
		foreach (var c in str)
		{
			if (XmlConvert.IsXmlChar(c))
			{
				sb.Append(c);
			}
			else
			{
				sb.Append(@"\u");
				sb.Append($@"{(uint)c:x4}");
			}
		}

		return sb.ToString();
	}
}
