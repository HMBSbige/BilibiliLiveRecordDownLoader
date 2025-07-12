namespace BilibiliLiveRecordDownLoader.Shared.Utils;

public static class Extensions
{
	public static string ToCookie(this IEnumerable<string> cookies)
	{
		HashSet<string> hashSet = [];

		foreach (string cookie in cookies)
		{
			string? keyValue = cookie.Split(';', StringSplitOptions.TrimEntries).FirstOrDefault();

			if (!string.IsNullOrEmpty(keyValue))
			{
				hashSet.Add(keyValue);
			}
		}

		return string.Join(';', hashSet);
	}

	public static string TryAddCookie(this string? cookies, string key, string value)
	{
		if (string.IsNullOrWhiteSpace(cookies))
		{
			return $@"{key}={value}";
		}

		Dictionary<string, string> pairs = [];

		foreach (string cookie in cookies.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			string[] pair = cookie.Split('=', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

			if (pair.Length is 2)
			{
				pairs.Add(pair[0], pair[1]);
			}
		}

		return pairs.TryAdd(key, value) ? string.Join(';', pairs.Select(x => $@"{x.Key}={x.Value}")) : cookies;
	}
}
