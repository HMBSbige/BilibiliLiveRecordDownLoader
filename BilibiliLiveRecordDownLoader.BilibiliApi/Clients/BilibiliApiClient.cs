using BilibiliApi.Utils;
using BilibiliLiveRecordDownLoader.Shared.Interfaces;
using BilibiliLiveRecordDownLoader.Shared.Utils;
using CryptoBase;
using CryptoBase.Abstractions.Digests;
using CryptoBase.Digests.MD5;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BilibiliApi.Clients
{
	public partial class BilibiliApiClient : IHttpClient
	{
		public HttpClient Client { get; set; }

		private static readonly SemaphoreSlim SemaphoreSlim = new(1, 1);

		public BilibiliApiClient(HttpClient client)
		{
			Client = client;
		}

		private async Task<T?> GetJsonAsync<T>(string url, CancellationToken token)
		{
			await SemaphoreSlim.WaitAsync(token);
			try
			{
				return await Client.GetFromJsonAsync<T>(url, token);
			}
			finally
			{
				SemaphoreSlim.Release();
			}
		}

		private async Task<HttpResponseMessage> PostAsync(string url, HttpContent content, CancellationToken token)
		{
			await SemaphoreSlim.WaitAsync(token);
			try
			{
				return await Client.PostAsync(url, content, token);
			}
			finally
			{
				SemaphoreSlim.Release();
			}
		}

		private async Task<HttpResponseMessage> PostAsync(string url, Dictionary<string, string> pair, bool isSign, CancellationToken token)
		{
			using var content = await GetBodyAsync(pair, isSign);
			return await PostAsync(url, content, token);
		}

		private static async ValueTask<FormUrlEncodedContent> GetBodyAsync(Dictionary<string, string> pair, bool isSign)
		{
			if (isSign)
			{
				pair[@"appkey"] = AppConstants.AppKey;
				pair[@"ts"] = Timestamp.GetTimestamp(DateTime.UtcNow).ToString();
				pair = pair.OrderBy(p => p.Key).ToDictionary(p => p.Key, o => o.Value);
				using var temp = new FormUrlEncodedContent(pair.Cast());
				var str = await temp.ReadAsStringAsync();
				var md5 = Md5String(str + AppConstants.AppSecret);
				pair.Add(@"sign", md5);
			}

			return new FormUrlEncodedContent(pair.Cast());
		}

		private static string Md5String(in string str)
		{
			var buffer = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetMaxByteCount(str.Length));
			try
			{
				var length = Encoding.UTF8.GetBytes(str, buffer);

				Span<byte> hash = stackalloc byte[HashConstants.Md5Length];

				MD5Utils.Default(buffer.AsSpan(0, length), hash);

				return hash.ToHex();
			}
			finally
			{
				ArrayPool<byte>.Shared.Return(buffer);
			}
		}
	}
}
