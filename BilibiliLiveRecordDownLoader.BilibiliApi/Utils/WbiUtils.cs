using System.Collections.Frozen;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace BilibiliApi.Utils;

public static class WbiUtils
{
	private static ReadOnlySpan<byte> MixinKeyEncTab => [46, 47, 18, 2, 53, 8, 23, 32, 15, 50, 10, 31, 58, 3, 45, 35, 27, 43, 5, 49, 33, 9, 42, 19, 29, 28, 14, 39, 12, 38, 41, 13, 37, 48, 7, 16, 24, 55, 40, 61, 26, 17, 0, 1, 60, 51, 30, 4, 22, 25, 54, 21, 56, 59, 6, 63, 57, 62, 11, 36, 20, 34, 44, 52];

	private static string GetMixinKey(string orig)
	{
		string result = string.Empty;

		foreach (int i in MixinKeyEncTab)
		{
			result += orig[i];
		}

		return result[..32];
	}

	public static async ValueTask SignAsync(Dictionary<string, string> parameters, (string imgKey, string subKey) wbiKey, DateTimeOffset ts, CancellationToken cancellationToken = default)
	{
		FrozenDictionary<string, string> tmp = parameters
			.Select(x => new KeyValuePair<string, string>(x.Key, RemoveSpecialChars(x.Value)))
			.Append(new KeyValuePair<string, string>(@"wts", ts.ToUnixTimeSeconds().ToString()))
			.OrderBy(x => x.Key)
			.ToFrozenDictionary(x => x.Key, x => x.Value);

		using FormUrlEncodedContent temp = new(tmp);
		string query = await temp.ReadAsStringAsync(cancellationToken);

		string wbiSign = (query + GetMixinKey(wbiKey.imgKey + wbiKey.subKey)).ToMd5HexString();
		parameters["w_rid"] = wbiSign;
		parameters[@"wts"] = tmp[@"wts"];

		return;

		string RemoveSpecialChars(string str)
		{
			DefaultInterpolatedStringHandler handler = new(default, str.Length);

			foreach (char c in str.Where(c => c is not ('!' or '\'' or '(' or ')' or '*')))
			{
				handler.AppendFormatted(c);
			}

			return handler.ToStringAndClear();
		}
	}

	public static async ValueTask<(string, string)> GetWbiKeyAsync(this HttpClient httpClient, CancellationToken cancellationToken = default)
	{
		const string url = @"https://api.bilibili.com/x/web-interface/nav";

		JsonElement json = await httpClient.GetFromJsonAsync<JsonElement>(url, cancellationToken);

		JsonElement data = json.GetProperty(@"data").GetProperty(@"wbi_img");

		Uri imgUrl = new(data.GetProperty(@"img_url").GetString()!);
		Uri subUrl = new(data.GetProperty(@"sub_url").GetString()!);

		string imgKey = Path.GetFileNameWithoutExtension(imgUrl.LocalPath);
		string subKey = Path.GetFileNameWithoutExtension(subUrl.LocalPath);

		return (imgKey, subKey);
	}
}
