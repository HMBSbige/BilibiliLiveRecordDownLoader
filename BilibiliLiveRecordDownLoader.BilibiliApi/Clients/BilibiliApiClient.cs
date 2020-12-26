using BilibiliLiveRecordDownLoader.Shared.Interfaces;
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
	}
}
