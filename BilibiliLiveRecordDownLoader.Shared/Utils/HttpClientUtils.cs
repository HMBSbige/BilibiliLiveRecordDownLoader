using BilibiliLiveRecordDownLoader.Shared.HttpPolicy;
using System;
using System.Net;
using System.Net.Http;

namespace BilibiliLiveRecordDownLoader.Shared.Utils
{
	public static class HttpClientUtils
	{
		public static HttpClient BuildClientForBilibili(string userAgent, string? cookie, TimeSpan timeout, bool useProxy)
		{
			var handle = new SocketsHttpHandler
			{
				UseProxy = useProxy,
				UseCookies = cookie is null or @""
			};
			var client = new HttpClient(handle, true);
			if (!handle.UseCookies)
			{
				client.DefaultRequestHeaders.Add(@"Cookie", cookie);
			}

			client.DefaultRequestVersion = HttpVersion.Version20;
			client.Timeout = timeout;
			client.DefaultRequestHeaders.Accept.ParseAdd(@"application/json, text/javascript, */*; q=0.01");
			client.DefaultRequestHeaders.Referrer = new Uri(@"https://live.bilibili.com/");
			client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);

			return client;
		}

		public static HttpClient BuildClientForMultiThreadedDownloader(string? cookie, string userAgent, bool useProxy)
		{
			var handle = new SocketsHttpHandler
			{
				UseProxy = useProxy,
				UseCookies = cookie is null or @""
			};
			var client = new HttpClient(new RetryHandler(handle, 10), true);
			if (!handle.UseCookies)
			{
				client.DefaultRequestHeaders.Add(@"Cookie", cookie);
			}

			client.DefaultRequestVersion = HttpVersion.Version20;
			client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
			client.DefaultRequestHeaders.ConnectionClose = false;

			return client;
		}
	}
}
