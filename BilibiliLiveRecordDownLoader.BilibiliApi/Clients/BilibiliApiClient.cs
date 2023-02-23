using BilibiliApi.Utils;
using BilibiliLiveRecordDownLoader.Shared.Interfaces;
using BilibiliLiveRecordDownLoader.Shared.Utils;
using CryptoBase.Abstractions.Digests;
using CryptoBase.DataFormatExtensions;
using CryptoBase.Digests;
using System.Buffers;
using System.Net.Http.Json;
using System.Text;

namespace BilibiliApi.Clients;

public partial class BilibiliApiClient : IHttpClient
{
	public HttpClient Client { get; set; }

	public BilibiliApiClient(HttpClient client)
	{
		Client = client;
	}

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
			string md5 = Md5String(str + AppConstants.AppSecret);
			pair.Add(@"sign", md5);
		}

		return new FormUrlEncodedContent(pair);
	}

	private static string Md5String(in string str)
	{
		byte[] buffer = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetMaxByteCount(str.Length));
		try
		{
			int length = Encoding.UTF8.GetBytes(str, buffer);

			Span<byte> hash = stackalloc byte[HashConstants.Md5Length];
			using IHash md5 = DigestUtils.Create(DigestType.Md5);
			md5.UpdateFinal(buffer.AsSpan(0, length), hash);

			return hash.ToHex();
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(buffer);
		}
	}
}
