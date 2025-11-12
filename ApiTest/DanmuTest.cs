using BilibiliApi.Enums;
using BilibiliApi.Utils;
using System.Text;

namespace ApiTest;

[TestClass]
public class DanmuTest
{
	[TestMethod]
	public void ParseTest()
	{
		const string json = """{"code":0}""";
		const string json1 = """{"cmd":"ROOM_CHANGE","data":{"title":"\u6d4b\u8bd5","area_id":236,"parent_area_id":6,"area_name":"\u4e3b\u673a\u6e38\u620f","parent_area_name":"\u5355\u673a"}}""";
		const string json2 = """{"cmd":"PREPARING","roomid":"40462"}""";
		const string json3 = """{"cmd":"LIVE","roomid":40462}""";
		const string json4 = """{"cmd":"ROOM_CHANGE","data":{"title":null,"area_name":null,"parent_area_name":null}}""";
		const string json5 = """{"cmd":"PREPARING","round":1,"roomid":"40462"}""";

		Assert.ThrowsExactly<KeyNotFoundException>(() => DanmuFactory.ParseJson(Encoding.UTF8.GetBytes(json)));

		Assert.AreEqual(LiveStatus.未知, DanmuFactory.ParseJson(Encoding.UTF8.GetBytes(json1)).IsStreaming());
		Assert.AreEqual(LiveStatus.闲置, DanmuFactory.ParseJson(Encoding.UTF8.GetBytes(json2)).IsStreaming());
		Assert.AreEqual(LiveStatus.直播, DanmuFactory.ParseJson(Encoding.UTF8.GetBytes(json3)).IsStreaming());
		Assert.AreEqual(LiveStatus.轮播, DanmuFactory.ParseJson(Encoding.UTF8.GetBytes(json5)).IsStreaming());

		Assert.AreEqual(@"测试", DanmuFactory.ParseJson(Encoding.UTF8.GetBytes(json1)).TitleChanged());
		Assert.IsNull(DanmuFactory.ParseJson(Encoding.UTF8.GetBytes(json2)).TitleChanged());
		Assert.IsNull(DanmuFactory.ParseJson(Encoding.UTF8.GetBytes(json3)).TitleChanged());
		Assert.AreEqual(string.Empty, DanmuFactory.ParseJson(Encoding.UTF8.GetBytes(json4)).TitleChanged());
	}
}
