using BilibiliLiveRecordDownLoader.Shared.HttpPolicy;
using System;
using System.Net.Http;

namespace BilibiliLiveRecordDownLoader.Shared.Utils
{
	public static class HttpClientUtils
	{
		public static HttpClient BuildClientForBilibili(string userAgent, string? cookie, TimeSpan timeout)
		{
			HttpClient client;
			if (string.IsNullOrEmpty(cookie))
			{
				client = new(new ForceHttp2Handler(new SocketsHttpHandler()), true);
			}
			else
			{
				client = new(new ForceHttp2Handler(new SocketsHttpHandler { UseCookies = false }), true);
				client.DefaultRequestHeaders.Add(@"Cookie", cookie);
			}

			client.Timeout = timeout;
			client.DefaultRequestHeaders.Add(@"Accept", @"application/json, text/javascript, */*; q=0.01");
			client.DefaultRequestHeaders.Add(@"Referer", @"https://live.bilibili.com/");
			client.DefaultRequestHeaders.Add(@"User-Agent", userAgent);

			return client;
		}

		public static HttpClient BuildClientForMultiThreadedDownloader(string? cookie = null, string userAgent = Constants.ChromeUserAgent)
		{
			var httpHandler = new SocketsHttpHandler();
			if (!string.IsNullOrEmpty(cookie))
			{
				httpHandler.UseCookies = false;
			}

			var client = new HttpClient(new RetryHandler(httpHandler, 10), true);
			if (!string.IsNullOrEmpty(cookie))
			{
				client.DefaultRequestHeaders.Add(@"Cookie", cookie);
			}

			client.DefaultRequestHeaders.Add(@"User-Agent", userAgent);
			client.DefaultRequestHeaders.ConnectionClose = false;

			return client;
		}
	}
}
