using BilibiliApi.Clients;
using BilibiliLiveRecordDownLoader.Shared.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static ApiTest.TestConstants;

#pragma warning disable 8602
namespace ApiTest;

[TestClass]
public class BilibiliMangaTest
{
	private readonly BilibiliApiClient _apiClient = new(HttpClientUtils.BuildClientForBilibili(UserAgents.BilibiliManga, Cookie, new SocketsHttpHandler()));

	[TestMethod]
	public async Task GetMangaClockInInfoTestAsync()
	{
		var message = await _apiClient.GetMangaClockInInfoAsync(AccessToken);
		Assert.AreEqual(0, message.code);
		Assert.IsTrue(message.data.day_count >= 0);
		Assert.IsTrue(message.data.status is 0 or 1);
	}

	[TestMethod]
	public async Task MangaClockInTestAsync()
	{
		var status = await _apiClient.MangaClockInAsync(AccessToken);
		Assert.IsTrue(status is null or true);
	}

	[TestMethod]
	public async Task ShareComicTestAsync()
	{
		var status = await _apiClient.ShareComicAsync(AccessToken);
		Assert.IsTrue(status is null or true);
	}
}
