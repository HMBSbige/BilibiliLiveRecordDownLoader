using BilibiliApi.Clients;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;

#nullable disable warnings
// ReSharper disable PossibleNullReferenceException

namespace UnitTest
{
	[TestClass]
	public class BilibiliApiTest
	{
		private readonly BililiveApiClient _apiClient = new();

		[TestMethod]
		public async Task GetLiveRecordUrlTestAsync()
		{
			var json = await _apiClient.GetLiveRecordUrlAsync(@"R12x411c7PL"); // 视频链接会过期
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
	}
}
