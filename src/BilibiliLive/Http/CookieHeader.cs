namespace BilibiliLive.Http;

internal static class CookieHeader
{
	public static string EnsureCookie(string? cookie, string name, string value)
	{
		if (string.IsNullOrWhiteSpace(cookie))
		{
			return $"{name}={value}";
		}

		if (Contains(cookie, name))
		{
			return cookie.Trim();
		}

		return $"{cookie.Trim().TrimEnd(';')}; {name}={value}";
	}

	public static bool Contains(string cookie, string name)
	{
		foreach (Range range in cookie.AsSpan().Split(';'))
		{
			ReadOnlySpan<char> pair = cookie.AsSpan(range);
			int separator = pair.IndexOf('=');

			if (separator > 0 && pair.Slice(0, separator).Trim().SequenceEqual(name))
			{
				return true;
			}
		}

		return false;
	}

	public static string FromSetCookie(IEnumerable<string> setCookieHeaders)
	{
		OrderedDictionary<string, string> cookies = new(StringComparer.Ordinal);

		foreach (string header in setCookieHeaders)
		{
			ReadOnlySpan<char> pair = header.AsSpan();
			int end = pair.IndexOf(';');

			if (end >= 0)
			{
				pair = pair.Slice(0, end);
			}

			int separator = pair.IndexOf('=');

			if (separator <= 0)
			{
				continue;
			}

			string name = pair.Slice(0, separator).Trim().ToString();

			if (name.Length is not 0)
			{
				cookies[name] = pair.Slice(separator + 1).Trim().ToString();
			}
		}

		return string.Join(';', cookies.Select(static cookie => $"{cookie.Key}={cookie.Value}"));
	}
}
