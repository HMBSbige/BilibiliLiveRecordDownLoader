using BilibiliLive.Danmu;
using System.Text;

namespace BilibiliLive.Tests.Danmu;

public class DanmuMessageParserTest
{
	private static DanmuMessage? Parse(string json)
	{
		return DanmuMessageParser.Parse(Encoding.UTF8.GetBytes(json));
	}

	[Test]
	[Arguments("""{"cmd":"LIVE","roomid":6154037}""")]
	[Arguments("""{"roomid":6154037,"live_key":"1","cmd":"LIVE"}""")]
	[Arguments("""{"cmd":"LIVE:suffix"}""")]
	public async Task LiveMapsToLiveStartedAsync(string json)
	{
		await Assert.That(Parse(json)).IsEqualTo(new DanmuMessage.LiveStarted());
	}

	[Test]
	[Arguments("""{"cmd":"PREPARING","roomid":"6154037"}""", false)]
	[Arguments("""{"cmd":"PREPARING","round":1,"roomid":"6154037"}""", true)]
	[Arguments("""{"cmd":"PREPARING:1:2","round":1}""", true)]
	public async Task PreparingMapsToLiveStoppedAsync(string json, bool isRound)
	{
		await Assert.That(Parse(json)).IsEqualTo(new DanmuMessage.LiveStopped(isRound));
	}

	[Test]
	public async Task RoomChangeMapsToRoomChangedAsync()
	{
		DanmuMessage? message = Parse("""{"cmd":"ROOM_CHANGE","data":{"title":"新标题","area_id":889,"parent_area_id":6,"area_name":"新游推荐","parent_area_name":"单机游戏","live_key":"0"}}""");

		await Assert.That(message).IsEqualTo(new DanmuMessage.RoomChanged("新标题", "新游推荐", "单机游戏"));
	}

	[Test]
	[Arguments("""{"cmd":"DANMU_MSG:4:0:2:2:2:0","info":[[0,1,25],"hi"]}""")]
	[Arguments("""{"cmd":"LIVE_INTERACTIVE_GAME","data":{}}""")]
	[Arguments("""{"cmd":"PREPARING_X"}""")]
	[Arguments("""{"cmd":"ROOM_CHANGE"}""")]
	[Arguments("""{"data":{"cmd":"LIVE"}}""")]
	[Arguments("""{"cmd":1}""")]
	[Arguments("""[]""")]
	public async Task OtherCommandsAreDroppedAsync(string json)
	{
		await Assert.That(Parse(json)).IsNull();
	}

	[Test]
	public async Task MalformedJsonThrowsJsonExceptionAsync()
	{
		await Assert.That(() => Parse("""{"cmd":"LIVE""")).Throws<System.Text.Json.JsonException>();
	}

	[Test]
	[Arguments("""{"code":0}""", 0L)]
	[Arguments("""{"code":-101}""", -101L)]
	public async Task AuthReplyCodeIsReadAsync(string json, long expected)
	{
		await Assert.That(DanmuMessageParser.GetAuthReplyCode(Encoding.UTF8.GetBytes(json))).IsEqualTo(expected);
	}

	[Test]
	[Arguments("""{}""")]
	[Arguments("""not json""")]
	public async Task AuthReplyWithoutCodeIsNullAsync(string json)
	{
		await Assert.That(DanmuMessageParser.GetAuthReplyCode(Encoding.UTF8.GetBytes(json))).IsNull();
	}
}
