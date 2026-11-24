using System.Globalization;

namespace BilibiliLive.Http;

internal static class CookieHeader
{
	public static string EnsureCookie(string? cookie, string name, string value)
	{
		if (string.IsNullOrWhiteSpace(cookie))
		{
			return $"{name}={value}";
		}

		if (GetValue(cookie, name) is not null)
		{
			return cookie.Trim();
		}

		return $"{cookie.Trim().TrimEnd(';')}; {name}={value}";
	}

	public static string? GetValue(string? cookie, string name)
	{
		if (cookie is null)
		{
			return null;
		}

		foreach (Range range in cookie.AsSpan().Split(';'))
		{
			ReadOnlySpan<char> pair = cookie.AsSpan(range);
			int separator = pair.IndexOf('=');

			if (separator > 0 && pair.Slice(0, separator).Trim().SequenceEqual(name))
			{
				return pair.Slice(separator + 1).Trim().ToString();
			}
		}

		return null;
	}

	public static string FromSetCookie(IEnumerable<string> setCookieHeaders)
	{
		OrderedDictionary<string, string> cookies = [];

		foreach (SetCookie cookie in ParseSetCookie(setCookieHeaders))
		{
			cookies[cookie.Name] = cookie.Value;
		}

		return Join(cookies);
	}

	public static DateTimeOffset? GetExpires(IEnumerable<string> setCookieHeaders, string name)
	{
		return ParseSetCookie(setCookieHeaders).LastOrDefault(cookie => cookie.Name == name)?.Expires;
	}

	// 原有条目保持顺序，同名的换成新值，新的追加在后面
	public static string Merge(string? cookie, string updates)
	{
		OrderedDictionary<string, string> cookies = [];

		// 同名的条目 GetValue 取第一个，这里也一样
		foreach ((string name, string value) in Parse(cookie))
		{
			cookies.TryAdd(name, value);
		}

		foreach ((string name, string value) in Parse(updates))
		{
			cookies[name] = value;
		}

		return Join(cookies);
	}

	private static IEnumerable<KeyValuePair<string, string>> Parse(string? cookie)
	{
		foreach (string pair in (cookie ?? string.Empty).Split(';'))
		{
			if (ParsePair(pair) is { } parsed)
			{
				yield return parsed;
			}
		}
	}

	private static IEnumerable<SetCookie> ParseSetCookie(IEnumerable<string> headers)
	{
		foreach (string header in headers)
		{
			string[] parts = header.Split(';');

			if (ParsePair(parts[0]) is not (var name, var value))
			{
				continue;
			}

			DateTimeOffset? expires = null;

			for (int i = 1; i < parts.Length; ++i)
			{
				if (ParsePair(parts[i]) is (var attribute, var date) && attribute.Equals("Expires", StringComparison.OrdinalIgnoreCase) && DateTimeOffset.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed))
				{
					expires = parsed;
				}
			}

			yield return new SetCookie(name, value, expires);
		}
	}

	private static KeyValuePair<string, string>? ParsePair(ReadOnlySpan<char> pair)
	{
		int separator = pair.IndexOf('=');

		if (separator <= 0)
		{
			return null;
		}

		ReadOnlySpan<char> name = pair.Slice(0, separator).Trim();
		return name.IsEmpty ? null : new KeyValuePair<string, string>(name.ToString(), pair.Slice(separator + 1).Trim().ToString());
	}

	private static string Join(OrderedDictionary<string, string> cookies)
	{
		return string.Join("; ", cookies.Select(static cookie => $"{cookie.Key}={cookie.Value}"));
	}

	private sealed record SetCookie(string Name, string Value, DateTimeOffset? Expires);
}
