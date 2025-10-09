using BilibiliApi.Clients;
using BilibiliApi.Enums;
using BilibiliApi.Model;
using BilibiliApi.Model.DanmuConf;
using BilibiliApi.Model.Login.QrCode.GetLoginUrl;
using BilibiliLiveRecordDownLoader.Shared.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;

namespace ApiTest;

[TestClass]
public class BilibiliApiTest
{
	private readonly BilibiliApiClient _apiClient = new(HttpClientUtils.BuildClientForBilibili(string.Empty, TestConstants.Cookie, new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.Brotli }));

	[TestMethod]
	public async Task GetDanmuConfTestAsync()
	{
		DanmuConfMessage? json = await _apiClient.GetDanmuConfAsync(732);
		Assert.IsNotNull(json);
		Assert.AreEqual(0, json.code);
		Assert.AreEqual(@"0", json.message);

		Assert.IsNotNull(json.data);
		Assert.IsNotNull(json.data.host_list);
		Assert.IsTrue(json.data.host_list.Length > 0);
		Assert.IsTrue(!string.IsNullOrWhiteSpace(json.data.token));

		Assert.AreEqual(@"broadcastlv.chat.bilibili.com", json.data.host_list.Last().host);
		Assert.AreEqual(2243, json.data.host_list.Last().port);
		Assert.IsTrue(json.data.host_list.Last().wss_port is 2245 or 443);
		Assert.AreEqual(2244, json.data.host_list.Last().ws_port);
	}

	[TestMethod]
	public async Task GetRoomUriTestAsync()
	{
		(Uri[] hlsUris, string format) = await _apiClient.GetRoomStreamUriAsync(6);

		Assert.AreNotEqual(0, hlsUris.Length);
		Assert.AreEqual(@"fmp4", format);

		foreach (Uri hlsUri in hlsUris)
		{
			Assert.AreEqual(Uri.UriSchemeHttps, hlsUri.Scheme);
			Assert.AreEqual(@".m3u8", Path.GetExtension(hlsUri.AbsolutePath));
			Console.WriteLine(hlsUri);
		}
	}

	[TestMethod]
	public async Task GetRoomInfoTestAsync()
	{
		LiveRoomInfo info = await _apiClient.GetLiveRoomInfoAsync(732);

		Assert.AreEqual(732, info.ShortId);
		Assert.AreEqual(6154037, info.RoomId);
		Assert.IsTrue(info.LiveStatus is LiveStatus.闲置 or LiveStatus.直播 or LiveStatus.轮播);
		Assert.IsNotNull(info.Title);
		Assert.AreEqual(194484313, info.UserId);
		Assert.AreEqual(@"Asaki大人", info.UserName);
	}

	[TestMethod]
	public async Task GetLoginUrlTestAsync()
	{
		GetLoginUrlMessage? json = await _apiClient.GetLoginUrlAsync();
		Assert.IsNotNull(json);
		Assert.AreEqual(json.code, 0);
		Assert.AreEqual(json.message, @"0");
		Assert.IsNotNull(json.data);

		Assert.IsNotNull(json.data.url);
		Assert.IsTrue(json.data.url.StartsWith(@"https://"));

		Assert.IsNotNull(json.data.qrcode_key);
		Assert.AreEqual(json.data.qrcode_key.Length, 32);
	}

	[TestMethod]
	public async Task GetLoginInfoTestAsync()
	{
		string cookie = await _apiClient.GetLoginInfoAsync(@"");// 设置 Key
		Assert.IsTrue(cookie.Contains(@"sid="));
		Assert.IsTrue(cookie.Contains(@"DedeUserID="));
		Assert.IsTrue(cookie.Contains(@"DedeUserID__ckMd5="));
		Assert.IsTrue(cookie.Contains(@"SESSDATA="));
		Assert.IsTrue(cookie.Contains(@"bili_jct="));
	}

	[TestMethod]
	public async Task GetLoginInfoFailTestAsync()
	{
		HttpRequestException ex = await Assert.ThrowsExactlyAsync<HttpRequestException>(async () => await _apiClient.GetLoginInfoAsync(string.Empty));
		Assert.AreEqual(ex.Message, @"不存在该密钥");
	}

	[TestMethod]
	public async Task CheckLoginStatusTestAsync()
	{
		Assert.AreNotEqual(await _apiClient.CheckLoginStatusAsync(), string.IsNullOrEmpty(TestConstants.Cookie));
	}

	[TestMethod]
	public async Task GetUidTestAsync()
	{
		Assert.IsTrue(await _apiClient.GetUidAsync() > 0);
	}

	[TestMethod]
	public async Task DanmuSendTestAsync()
	{
		await _apiClient.SendDanmuAsync(40462, TestConstants.Csrf);
	}
}
