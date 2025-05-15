namespace BilibiliLiveRecordDownLoader.Shared.Utils;

public static class Extensions
{
	public static string ToCookie(this IEnumerable<string> cookies)
	{
		HashSet<string> hashSet = new();

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
}
