using BilibiliApi.Clients;
using BilibiliLiveRecordDownLoader.Shared.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using static ApiTest.TestConstants;

#pragma warning disable 8602
namespace ApiTest
{
	[TestClass]
	public class BilibiliApiTest
	{
		private readonly BilibiliApiClient _apiClient = new(HttpClientUtils.BuildClientForBilibili(string.Empty, Cookie, new SocketsHttpHandler()));
		private const string Rid = @"R1Bx411w7Wk"; // 视频链接会过期

		[TestMethod]
		public async Task GetLiveRecordUrlTestAsync()
		{
			var json = await _apiClient.GetLiveRecordUrlAsync(Rid);
			Assert.AreEqual(json.code, 0);
			Assert.AreEqual(json.message, @"0");
			Assert.IsTrue(json.data.size > 0);
			Assert.IsTrue(json.data.length > 0);
			Assert.IsTrue(json.data.list.Length > 0);
		}

		[TestMethod]
		public async Task GetRoomInitTestAsync()
		{
			var json = await _apiClient.GetRoomInitAsync(732);
			Assert.AreEqual(json.code, 0);
			Assert.AreEqual(json.msg, @"ok");
			Assert.AreEqual(json.message, @"ok");
			Assert.AreEqual(json.data.room_id, 6154037);
			Assert.AreEqual(json.data.short_id, 732);
		}

		[TestMethod]
		public async Task GetLiveRecordListTestAsync()
		{
			var json = await _apiClient.GetLiveRecordListAsync(6154037);
			Assert.AreEqual(json.code, 0);
			Assert.AreEqual(json.message, @"0");

			var all = json.data.count;
			Assert.IsTrue(all > 0);
			json = await _apiClient.GetLiveRecordListAsync(6154037, 1, all);

			Assert.AreEqual(json.code, 0);
			Assert.AreEqual(json.message, @"0");
			Assert.IsTrue(json.data.count > 0);
			Assert.IsTrue(json.data.list.Length > 0);
			Assert.IsTrue(json.data.list.Length == all);

			foreach (var data in json.data.list)
			{
				Assert.IsFalse(string.IsNullOrEmpty(data.rid));
				Assert.IsFalse(string.IsNullOrEmpty(data.title));
				Assert.IsFalse(string.IsNullOrEmpty(data.cover));
				Assert.IsFalse(string.IsNullOrEmpty(data.area_name));
				Assert.IsFalse(string.IsNullOrEmpty(data.parent_area_name));
				Assert.IsTrue(data.start_timestamp > 0);
				Assert.IsTrue(data.end_timestamp > 0);
				Assert.IsTrue(data.end_timestamp > data.start_timestamp);
				Assert.IsTrue(data.online > 0);
				Assert.IsTrue(data.danmu_num > 0);
				Assert.IsTrue(data.length > 0);
				Assert.IsTrue((data.end_timestamp - data.start_timestamp) * 1000 >= data.length);
			}
		}

		[TestMethod]
		public async Task GetAnchorInfoTestAsync()
		{
			var json = await _apiClient.GetAnchorInfoAsync(732);
			Assert.AreEqual(json.code, 0);
			Assert.AreEqual(json.msg, @"success");
			Assert.AreEqual(json.message, @"success");
			Assert.AreEqual(json.data.info.uid, 194484313);
			Assert.AreEqual(json.data.info.uname, @"Asaki大人");
			Assert.IsTrue(json.data.info.face.StartsWith(@"https://"));
			Assert.AreEqual(json.data.info.platform_user_level, 6);
		}

		[TestMethod]
		public async Task GetDanmuConfTestAsync()
		{
			var json = await _apiClient.GetDanmuConfAsync(732);
			Assert.AreEqual(json.code, 0);
			Assert.AreEqual(json.msg, @"ok");
			Assert.AreEqual(json.message, @"ok");
			Assert.AreEqual(json.data.port, 2243);
			Assert.AreEqual(json.data.host, @"broadcastlv.chat.bilibili.com");
			Assert.IsTrue(json.data.host_server_list.Length > 0);
			Assert.IsTrue(json.data.server_list.Length > 0);
			Assert.IsTrue(!string.IsNullOrWhiteSpace(json.data.token));
		}

		[TestMethod]
		public async Task GetPlayUrlTestAsync()
		{
			var json = await _apiClient.GetPlayUrlAsync(732);
			Assert.AreEqual(json.code, 0);
			Assert.AreEqual(json.message, @"0");
			Assert.AreEqual(json.data.current_qn, 10000);
			Assert.IsTrue(json.data.quality_description.Length > 0);
			Assert.IsTrue(json.data.durl.Length > 0);
			foreach (var durl in json.data.durl)
			{
				Assert.IsTrue(durl.url.StartsWith(@"https://"));
				Console.WriteLine(durl.url);
			}
		}

		[TestMethod]
		public async Task GetRoomInfoTestAsync()
		{
			var json = await _apiClient.GetRoomInfoAsync(732);
			Assert.AreEqual(json.code, 0);
			Assert.AreEqual(json.msg, @"ok");
			Assert.AreEqual(json.message, @"ok");
			Assert.AreEqual(json.data.room_id, 6154037);
			Assert.AreEqual(json.data.short_id, 732);
			Assert.IsTrue(json.data.live_status is 0 or 1 or 2);
			Assert.IsTrue(!string.IsNullOrWhiteSpace(json.data.title));
		}

		[TestMethod]
		public async Task GetLoginUrlTestAsync()
		{
			var json = await _apiClient.GetLoginUrlAsync();
			Assert.AreEqual(json.code, 0);
			Assert.AreEqual(json.status, true);
			Assert.IsTrue(json.data.url.StartsWith(@"https://"));
			Assert.AreEqual(json.data.oauthKey.Length, 32);
		}

		[TestMethod]
		public async Task GetLoginInfoTestAsync()
		{
			var cookie = await _apiClient.GetLoginInfoAsync(@""); // 设置 Key
			Assert.IsTrue(cookie.Contains(@"sid="));
			Assert.IsTrue(cookie.Contains(@"DedeUserID="));
			Assert.IsTrue(cookie.Contains(@"DedeUserID__ckMd5="));
			Assert.IsTrue(cookie.Contains(@"SESSDATA="));
			Assert.IsTrue(cookie.Contains(@"bili_jct="));
		}

		[TestMethod]
		public async Task GetLoginInfoFailTestAsync()
		{
			var ex = await Assert.ThrowsExceptionAsync<HttpRequestException>(async () => await _apiClient.GetLoginInfoAsync(string.Empty));
			Assert.AreEqual(ex.Message, @"不存在该密钥");
		}

		[TestMethod]
		public async Task CheckLoginStatusTestAsync()
		{
			Assert.AreNotEqual(await _apiClient.CheckLoginStatusAsync(), string.IsNullOrEmpty(Cookie));
		}

		[TestMethod]
		public async Task GetUidTestAsync()
		{
			Assert.IsTrue(await _apiClient.GetUidAsync() > 0);
		}

		[TestMethod]
		public async Task FansMedalTestAsync()
		{
			var message0 = await _apiClient.GetLiveFansMedalMessageAsync();
			Assert.AreEqual(0, message0.code);
			var count = message0.data.count;

			var list = await _apiClient.GetLiveFansMedalListAsync();
			Assert.AreEqual(count, list.Count);
		}

		[TestMethod]
		public async Task DanmuSendTestAsync()
		{
			await _apiClient.SendDanmuAsync(40462, Csrf);
		}

		[TestMethod]
		public async Task GetDanmuInfoByLiveRecordTestAsync()
		{
			var danmuInfo = await _apiClient.GetDanmuInfoByLiveRecordAsync(Rid);
			Assert.IsNotNull(danmuInfo);
			Assert.AreEqual(0L, danmuInfo.code);
			Assert.AreEqual(@"0", danmuInfo.message);
			Assert.IsNotNull(danmuInfo.data?.dm_info);

			var totalIndex = danmuInfo.data.dm_info.num;
			Assert.IsTrue(totalIndex > 0);

			var d = 0L;
			var ix = 0L;
			for (var i = 0L; i < totalIndex; ++i)
			{
				var danmuMsgInfo = await _apiClient.GetDmMsgByPlayBackIdAsync(Rid, i);
				Assert.IsNotNull(danmuMsgInfo.data?.dm);

				if (danmuMsgInfo.data?.dm?.dm_info is not null)
				{
					d += danmuMsgInfo.data.dm.dm_info.Length;
				}

				if (danmuMsgInfo.data.dm.interactive_info is not null)
				{
					ix += danmuMsgInfo.data.dm.interactive_info.Length;
				}
			}

			Debug.WriteLine($@"{d} 条弹幕");
			Debug.WriteLine($@"{ix} 条礼物");

			Assert.AreEqual(danmuInfo.data.dm_info.total_num, d + ix);
		}
	}
}
