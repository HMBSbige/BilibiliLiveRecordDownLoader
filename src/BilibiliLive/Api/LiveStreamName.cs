namespace BilibiliLive.Api;

// 流地址的路径是 /live-bvc/<n>/<流名>.flv、/live-bvc/<n>/<流名>.m3u8 或 /live-bvc/<n>/<流名>/index.m3u8
internal static class LiveStreamName
{
	private const string Root = "/live-bvc/";

	public static string? Parse(ReadOnlySpan<char> path)
	{
		if (!path.StartsWith(Root, StringComparison.Ordinal))
		{
			return null;
		}

		ReadOnlySpan<char> rest = path.Slice(Root.Length);
		int slash = rest.IndexOf('/');

		if (slash <= 0)
		{
			return null;
		}

		ReadOnlySpan<char> file = rest.Slice(slash + 1);
		int end = file.IndexOfAny('/', '.');
		return end > 0 && file.Slice(end) is ".flv" or ".m3u8" or "/index.m3u8" ? file.Slice(0, end).ToString() : null;
	}
}
