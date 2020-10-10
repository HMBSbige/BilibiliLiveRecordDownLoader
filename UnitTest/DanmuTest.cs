using BilibiliApi.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Text;

namespace UnitTest
{
    [TestClass]
    public class DanmuTest
    {
        [TestMethod]
        public void ParseTest()
        {
            const string json = @"{""code"":0}";
            const string json1 = @"{""cmd"":""ROOM_CHANGE"",""data"":{""title"":""\u6d4b\u8bd5"",""area_id"":236,""parent_area_id"":6,""area_name"":""\u4e3b\u673a\u6e38\u620f"",""parent_area_name"":""\u5355\u673a""}}";
            const string json2 = @"{""cmd"":""PREPARING"",""roomid"":""40462""}";
            const string json3 = @"{""cmd"":""LIVE"",""roomid"":40462}";

            Assert.ThrowsException<KeyNotFoundException>(() =>
            {
                DanmuFactory.ParseJson(Encoding.UTF8.GetBytes(json));
            });

            Assert.AreEqual(DanmuFactory.ParseJson(Encoding.UTF8.GetBytes(json1)).IsStreaming(), null);
            Assert.AreEqual(DanmuFactory.ParseJson(Encoding.UTF8.GetBytes(json2)).IsStreaming(), false);
            Assert.AreEqual(DanmuFactory.ParseJson(Encoding.UTF8.GetBytes(json3)).IsStreaming(), true);

            Assert.AreEqual(DanmuFactory.ParseJson(Encoding.UTF8.GetBytes(json1)).TitleChanged(), @"测试");
            Assert.AreEqual(DanmuFactory.ParseJson(Encoding.UTF8.GetBytes(json2)).TitleChanged(), null);
            Assert.AreEqual(DanmuFactory.ParseJson(Encoding.UTF8.GetBytes(json3)).TitleChanged(), null);
        }
    }
}
