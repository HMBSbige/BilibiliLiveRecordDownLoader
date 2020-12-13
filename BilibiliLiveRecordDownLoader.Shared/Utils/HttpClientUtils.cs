using BilibiliLiveRecordDownLoader.Shared.HttpPolicy;
using System;
using System.Net;
using System.Net.Http;

namespace BilibiliLiveRecordDownLoader.Shared.Utils
{
	public static class HttpClientUtils
	{
		public static HttpClient BuildClientForBilibili(string userAgent, string? cookie, HttpMessageHandler handler)
		{
			if (string.IsNullOrEmpty(userAgent))
			{
				userAgent = UserAgents.Chrome;
			}
			var client = new HttpClient(handler, false);
			if (!string.IsNullOrWhiteSpace(cookie))
			{
				client.DefaultRequestHeaders.Add(@"Cookie", cookie);
			}

			client.DefaultRequestVersion = HttpVersion.Version20;
			client.Timeout = TimeSpan.FromSeconds(10);
			client.DefaultRequestHeaders.Accept.ParseAdd(@"application/json, text/javascript, */*; q=0.01");
			client.DefaultRequestHeaders.Referrer = new(@"https://live.bilibili.com/");
			client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);

			return client;
		}

		public static HttpClient BuildClientForMultiThreadedDownloader(string? cookie, string userAgent, HttpMessageHandler handler)
		{
			if (string.IsNullOrEmpty(userAgent))
			{
				userAgent = UserAgents.Idm;
			}
			var client = new HttpClient(new RetryHandler(handler, 10), false);
			if (!string.IsNullOrWhiteSpace(cookie))
			{
				client.DefaultRequestHeaders.Add(@"Cookie", cookie);
			}

			client.DefaultRequestVersion = HttpVersion.Version20;
			client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
			client.DefaultRequestHeaders.ConnectionClose = false;

			return client;
		}

		public static HttpClient BuildClient(string? cookie, string userAgent, HttpMessageHandler handler)
		{
			if (string.IsNullOrEmpty(userAgent))
			{
				userAgent = UserAgents.Chrome;
			}

			var client = new HttpClient(handler, false);
			if (!string.IsNullOrWhiteSpace(cookie))
			{
				client.DefaultRequestHeaders.Add(@"Cookie", cookie);
			}

			client.DefaultRequestVersion = HttpVersion.Version20;
			client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);

			return client;
		}
	}
}
