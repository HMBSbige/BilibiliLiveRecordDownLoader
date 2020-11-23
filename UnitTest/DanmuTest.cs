using BilibiliApi.Enums;
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
			const string json4 = @"{""cmd"":""ROOM_CHANGE"",""data"":{""title"":null,""area_name"":null,""parent_area_name"":null}}";
			const string json5 = @"{""cmd"":""PREPARING"",""round"":1,""roomid"":""40462""}";

			Assert.ThrowsException<KeyNotFoundException>(() => DanmuFactory.ParseJson(Encoding.UTF8.GetBytes(json)));

			Assert.AreEqual(DanmuFactory.ParseJson(Encoding.UTF8.GetBytes(json1)).IsStreaming(), LiveStatus.未知);
			Assert.AreEqual(DanmuFactory.ParseJson(Encoding.UTF8.GetBytes(json2)).IsStreaming(), LiveStatus.闲置);
			Assert.AreEqual(DanmuFactory.ParseJson(Encoding.UTF8.GetBytes(json3)).IsStreaming(), LiveStatus.直播);
			Assert.AreEqual(DanmuFactory.ParseJson(Encoding.UTF8.GetBytes(json5)).IsStreaming(), LiveStatus.轮播);

			Assert.AreEqual(DanmuFactory.ParseJson(Encoding.UTF8.GetBytes(json1)).TitleChanged(), @"测试");
			Assert.AreEqual(DanmuFactory.ParseJson(Encoding.UTF8.GetBytes(json2)).TitleChanged(), null);
			Assert.AreEqual(DanmuFactory.ParseJson(Encoding.UTF8.GetBytes(json3)).TitleChanged(), null);
			Assert.AreEqual(DanmuFactory.ParseJson(Encoding.UTF8.GetBytes(json4)).TitleChanged(), string.Empty);
		}
	}
}
