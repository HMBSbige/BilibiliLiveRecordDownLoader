using BilibiliApi.Utils;
using BilibiliLiveRecordDownLoader.Shared.Interfaces;
using BilibiliLiveRecordDownLoader.Shared.Utils;
using System.Net.Http.Json;

namespace BilibiliApi.Clients;

public partial class BilibiliApiClient(HttpClient client) : IHttpClient
{
	public HttpClient Client { get; set; } = client;

	private async Task<T?> GetJsonAsync<T>(string url, CancellationToken token)
	{
		return await Client.GetFromJsonAsync<T>(url, token);
	}

	private async Task<HttpResponseMessage> PostAsync(string url, HttpContent content, CancellationToken token)
	{
		return await Client.PostAsync(url, content, token);
	}

	private async Task<HttpResponseMessage> PostAsync(string url, Dictionary<string, string> pair, bool isSign, CancellationToken token)
	{
		using FormUrlEncodedContent content = await GetBodyAsync(pair, isSign);
		return await PostAsync(url, content, token);
	}

	private static async ValueTask<FormUrlEncodedContent> GetBodyAsync(Dictionary<string, string> pair, bool isSign)
	{
		if (isSign)
		{
			pair[@"appkey"] = AppConstants.AppKey;
			pair[@"ts"] = Timestamp.GetTimestamp(DateTime.UtcNow).ToString();
			pair = pair.OrderBy(p => p.Key).ToDictionary(p => p.Key, o => o.Value);
			using FormUrlEncodedContent temp = new(pair);
			string str = await temp.ReadAsStringAsync();
			string md5 = (str + AppConstants.AppSecret).ToMd5HexString();
			pair.Add(@"sign", md5);
		}

		return new FormUrlEncodedContent(pair);
	}
}
