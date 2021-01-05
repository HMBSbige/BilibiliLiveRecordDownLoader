using BilibiliApi.Utils;
using BilibiliLiveRecordDownLoader.Shared.Interfaces;
using BilibiliLiveRecordDownLoader.Shared.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
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
			using var content = await GetBody(pair, isSign);
			return await PostAsync(url, content, token);
		}

		private static async ValueTask<FormUrlEncodedContent> GetBody(Dictionary<string, string> pair, bool isSign)
		{
			if (isSign)
			{
				pair[@"appkey"] = AppConstants.AppKey;
				pair[@"ts"] = Timestamp.GetTimestamp(DateTime.UtcNow).ToString();
				pair = pair.OrderBy(p => p.Key).ToDictionary(p => p.Key, o => o.Value);
				using var temp = new FormUrlEncodedContent(pair.Cast());
				var str = await temp.ReadAsStringAsync();
				var md5 = Md5.ComputeHash(str + AppConstants.AppSecret);
				pair.Add(@"sign", md5);
			}

			return new FormUrlEncodedContent(pair.Cast());
		}
	}
}
