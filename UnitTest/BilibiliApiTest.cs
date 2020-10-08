using BilibiliApi.Clients;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;

namespace UnitTest
{
    [TestClass]
    public class BilibiliApiTest
    {
        [TestMethod]
        public async Task GetLiveRecordUrlTestAsync()
        {
            using var client = new BililiveApiClient();
            var json = await client.GetLiveRecordUrl(@"R1nx411c7b1"); // 视频链接会过期
            Assert.AreEqual(json.code, 0);
            Assert.AreEqual(json.message, @"0");
            Assert.AreEqual(json.data.size, 13584350482);
            Assert.AreEqual(json.data.length, 18097042);
            Assert.AreEqual(json.data.list.Length, 11);
        }

        [TestMethod]
        public async Task GetRoomInitTestAsync()
        {
            using var client = new BililiveApiClient();
            var json = await client.GetRoomInit(732);
            Assert.AreEqual(json.code, 0);
            Assert.AreEqual(json.msg, @"ok");
            Assert.AreEqual(json.message, @"ok");
            Assert.AreEqual(json.data.room_id, 6154037);
            Assert.AreEqual(json.data.short_id, 732);
            Assert.AreEqual(json.data.uid, 194484313);
        }

        [TestMethod]
        public async Task GetLiveRecordListTestAsync()
        {
            using var client = new BililiveApiClient();
            var json = await client.GetLiveRecordList(6154037);
            Assert.AreEqual(json.code, 0);
            Assert.AreEqual(json.message, @"0");

            var all = json.data.count;
            Assert.IsTrue(all > 0);
            json = await client.GetLiveRecordList(6154037, 1, all);

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
            using var client = new BililiveApiClient();
            var json = await client.GetAnchorInfo(732);
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
            using var client = new BililiveApiClient();
            var json = await client.GetDanmuConf(6154037);
            Assert.AreEqual(json.code, 0);
            Assert.AreEqual(json.msg, @"ok");
            Assert.AreEqual(json.message, @"ok");
            Assert.AreEqual(json.data.port, 2243);
            Assert.AreEqual(json.data.host, @"broadcastlv.chat.bilibili.com");
            Assert.IsTrue(json.data.host_server_list.Length > 0);
            Assert.IsTrue(json.data.server_list.Length > 0);
            Assert.IsTrue(!string.IsNullOrWhiteSpace(json.data.token));
        }
    }
}
