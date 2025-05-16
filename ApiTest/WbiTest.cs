using BilibiliApi.Utils;
using CryptoBase.DataFormatExtensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;

namespace ApiTest;

[TestClass]
public class WbiTest
{
	[TestMethod]
	public async Task GetWbiKeyTestAsync()
	{
		HttpClient httpClient = new(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.Brotli });
		httpClient.DefaultRequestVersion = HttpVersion.Version30;

		(string imgKey, string subKey) = await httpClient.GetWbiKeyAsync();

		Assert.AreEqual(16, imgKey.FromHex().Length);
		Assert.AreEqual(16, subKey.FromHex().Length);
	}

	[TestMethod]
	public async Task GetWbiSignTestAsync()
	{
		Dictionary<string, string> query = new()
		{
			["foo"] = "11!4",
			["bar"] = "51'4",
			["zab"] = @"19)1(981*0"
		};

		(string imgKey, string subKey) = (@"7cd084941338484aae1ad9425b84077c", @"4932caff0ff746eab6f01bf08b70ac45");
		DateTimeOffset ts = DateTimeOffset.FromUnixTimeSeconds(1702204169);

		await WbiUtils.SignAsync(query, (imgKey, subKey), ts);

		Assert.AreEqual(5, query.Count);
		Assert.AreEqual("1702204169", query["wts"]);
		Assert.AreEqual("8f6f2b5b3d485fe1886cec6a0be8c5d4", query["w_rid"]);
	}
}
