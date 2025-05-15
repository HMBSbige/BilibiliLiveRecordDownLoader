using BilibiliLiveRecordDownLoader.Shared.HttpPolicy;
using System.Net;

namespace BilibiliLiveRecordDownLoader.Shared.Utils;

public static class HttpClientUtils
{
	public static HttpClient BuildClientForBilibili(string userAgent, string? cookie, HttpMessageHandler handler)
	{
		if (string.IsNullOrEmpty(userAgent))
		{
			userAgent = @"Mozilla/5.0";
		}

		HttpClient client = new(handler, false);

		if (!string.IsNullOrWhiteSpace(cookie))
		{
			client.DefaultRequestHeaders.Add(@"Cookie", cookie);
		}

		client.DefaultRequestVersion = HttpVersion.Version30;
		client.Timeout = TimeSpan.FromSeconds(10);
		client.DefaultRequestHeaders.Accept.ParseAdd(@"application/json, text/javascript, */*; q=0.01");
		client.DefaultRequestHeaders.Referrer = new Uri(@"https://live.bilibili.com/");
		client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);

		return client;
	}

	public static HttpClient BuildClientForMultiThreadedDownloader(string? cookie, string userAgent, HttpMessageHandler handler)
	{
		if (string.IsNullOrEmpty(userAgent))
		{
			userAgent = UserAgents.Idm;
		}

		HttpClient client = new(new RetryHandler(handler, 10), false);

		if (!string.IsNullOrWhiteSpace(cookie))
		{
			client.DefaultRequestHeaders.Add(@"Cookie", cookie);
		}

		client.DefaultRequestVersion = HttpVersion.Version30;
		client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
		client.DefaultRequestHeaders.ConnectionClose = false;

		return client;
	}

	public static HttpClient BuildClient(string? cookie, string userAgent, HttpMessageHandler handler)
	{
		if (string.IsNullOrEmpty(userAgent))
		{
			userAgent = @"Mozilla/5.0";
		}

		HttpClient client = new(handler, false);

		if (!string.IsNullOrWhiteSpace(cookie))
		{
			client.DefaultRequestHeaders.Add(@"Cookie", cookie);
		}

		client.DefaultRequestVersion = HttpVersion.Version30;
		client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);

		return client;
	}
}
