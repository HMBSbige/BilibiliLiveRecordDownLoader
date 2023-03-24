using System.Runtime.CompilerServices;
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

		try
		{
			return XmlConvert.VerifyXmlChars(str);
		}
		catch (XmlException)
		{
			DefaultInterpolatedStringHandler handler = new(2, str.Length);
			foreach (char c in str)
			{
				if (XmlConvert.IsXmlChar(c))
				{
					handler.AppendFormatted(c);
				}
				else
				{
					handler.AppendLiteral(@"\u");
					handler.AppendFormatted((int)c, @"x4");
				}
			}

			return handler.ToStringAndClear();
		}
	}
}
