using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace BilibiliLive.Wbi;

internal static class WbiSigner
{
	private const int MixinKeyLength = 32;

	private static ReadOnlySpan<byte> MixinKeyEncTab => [46, 47, 18, 2, 53, 8, 23, 32, 15, 50, 10, 31, 58, 3, 45, 35, 27, 43, 5, 49, 33, 9, 42, 19, 29, 28, 14, 39, 12, 38, 41, 13, 37, 48, 7, 16, 24, 55, 40, 61, 26, 17, 0, 1, 60, 51, 30, 4, 22, 25, 54, 21, 56, 59, 6, 63, 57, 62, 11, 36, 20, 34, 44, 52];

	public static string? TryGetMixinKey(string imgKey, string subKey)
	{
		string raw = imgKey + subKey;

		if (raw.Length < MixinKeyEncTab.Length)
		{
			return null;
		}

		return string.Create(MixinKeyLength, raw, static (span, raw) =>
		{
			ReadOnlySpan<byte> table = MixinKeyEncTab;

			for (int i = 0; i < span.Length; ++i)
			{
				span[i] = raw[table[i]];
			}
		});
	}

	public static string Sign(IEnumerable<KeyValuePair<string, string>> parameters, string mixinKey, long wts)
	{
		IEnumerable<KeyValuePair<string, string>> all = parameters
			.Where(static p => p.Key is not ("wts" or "w_rid"))
			.Append(new KeyValuePair<string, string>("wts", wts.ToString(CultureInfo.InvariantCulture)))
			.OrderBy(static p => p.Key, StringComparer.Ordinal);

		string query = string.Join('&', all.Select(static p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(RemoveReservedChars(p.Value))}"));

		string wRid = Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(query + mixinKey)));

		return $"{query}&w_rid={wRid}";
	}

	// The web client strips these because encodeURIComponent leaves them unescaped
	private static string RemoveReservedChars(string value)
	{
		return value.AsSpan().IndexOfAny("!'()*") < 0 ? value : string.Concat(value.Where(static c => c is not ('!' or '\'' or '(' or ')' or '*')));
	}
}
