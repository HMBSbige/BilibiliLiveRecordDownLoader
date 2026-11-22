using BilibiliLive.Danmu;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BilibiliLive.Tests.Danmu;

public class DanmuMessageParserTest
{
	private static readonly DanmuReceiveTime ReceivedAt = new(42, new DateTimeOffset(2026, 11, 22, 10, 0, 0, TimeSpan.Zero));

	private static List<DanmuMessage> Parse(byte[] json, DanmuParseMode mode)
	{
		List<DanmuMessage> output = [];
		DanmuMessageParser.Parse(json, mode, ReceivedAt, output);
		return output;
	}

	private static DanmuMessage? Parse(string json, DanmuParseMode mode = DanmuParseMode.None)
	{
		return Parse(Encoding.UTF8.GetBytes(json), mode).SingleOrDefault();
	}

	// Captured anonymously from live rooms, with uids, nicknames and avatars replaced by fake values
	internal static byte[] Fixture(string name)
	{
		return File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Danmu", name));
	}

	private static List<DanmuMessage> ParseFixture(string name)
	{
		return Parse(Fixture(name), DanmuParseMode.Chat);
	}

	private static T Received<T>(T message) where T : DanmuMessage
	{
		return message with { ReceivedAt = ReceivedAt };
	}

	private static string Modify(string fixture, Action<JsonNode> modify)
	{
		JsonNode root = JsonNode.Parse(Fixture(fixture)) ?? throw new InvalidDataException();
		modify(root);
		return root.ToJsonString();
	}

	[Test]
	[Arguments("""{"cmd":"LIVE","roomid":6154037}""")]
	[Arguments("""{"roomid":6154037,"live_key":"1","cmd":"LIVE"}""")]
	[Arguments("""{"cmd":"LIVE:suffix"}""")]
	public async Task LiveMapsToLiveStartedAsync(string json)
	{
		await Assert.That(Parse(json)).IsEqualTo(Received(new DanmuMessage.LiveStarted()));
	}

	[Test]
	[Arguments("""{"cmd":"PREPARING","roomid":"6154037"}""", false)]
	[Arguments("""{"cmd":"PREPARING","round":1,"roomid":"6154037"}""", true)]
	[Arguments("""{"cmd":"PREPARING:1:2","round":1}""", true)]
	public async Task PreparingMapsToLiveStoppedAsync(string json, bool isRound)
	{
		await Assert.That(Parse(json)).IsEqualTo(Received(new DanmuMessage.LiveStopped(isRound)));
	}

	[Test]
	public async Task RoomChangeMapsToRoomChangedAsync()
	{
		DanmuMessage? message = Parse("""{"cmd":"ROOM_CHANGE","data":{"title":"新标题","area_id":889,"parent_area_id":6,"area_name":"新游推荐","parent_area_name":"单机游戏","live_key":"0"}}""");

		await Assert.That(message).IsEqualTo(Received(new DanmuMessage.RoomChanged("新标题", "新游推荐", "单机游戏")));
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task LoginNoticeIsParsedWhetherOrNotChatIsIncludedAsync(bool includeChat)
	{
		List<DanmuMessage> messages = Parse(Fixture("log_in_notice.json"), includeChat ? DanmuParseMode.Chat : DanmuParseMode.None);

		await Sequences.AssertEqualAsync(messages, [Received(new DanmuMessage.LoginNotice("为保护用户隐私，未登录无法查看他人昵称"))]);
	}

	[Test]
	[Arguments("""{"cmd":"DANMU_MSG:4:0:2:2:2:0","info":[[0,1,25],"hi"]}""")]
	[Arguments("""{"cmd":"SUPER_CHAT_MESSAGE","data":{"price":30}}""")]
	[Arguments("""{"cmd":"SEND_GIFT_V2","data":{"pb":"not base64"}}""")]
	[Arguments("""{"cmd":"USER_TOAST_MSG_V2","data":{}}""")]
	[Arguments("""{"cmd":"SUPER_CHAT_MESSAGE_DELETE","data":{"ids":[1]}}""")]
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
	public async Task ChatIsDroppedWithoutBeingParsedWhenNotIncludedAsync()
	{
		// Malformed after the cmd, so reaching the parser would throw
		await Assert.That(Parse("""{"cmd":"DANMU_MSG","info":[[0,1,""")).IsNull();
	}

	[Test]
	public async Task MalformedJsonThrowsJsonExceptionAsync()
	{
		await Assert.That(() => Parse("""{"cmd":"LIVE""")).Throws<JsonException>();
	}

	[Test]
	public async Task MaskedCommentIsParsedAsync()
	{
		List<DanmuMessage> messages = ParseFixture("comment_masked.json");

		DanmuMessage.Comment expected = new
		(
			new DanmuSender(0, "测1***", GuardLevel.None, new DanmuMedal(18, "i奶芙", 13081892, 10000001, 1939021227, true)),
			"666",
			1,
			25,
			16777215,
			DateTimeOffset.FromUnixTimeMilliseconds(1795344443482),
			"585338f944da18e35778a19c5e6b02c82034",
			"d665fa75",
			DanmuCommentKind.Text,
			false
		);

		await Sequences.AssertEqualAsync(messages, [Received(expected)]);
	}

	[Test]
	public async Task CommentOfGuardMemberIsParsedAsync()
	{
		List<DanmuMessage> messages = ParseFixture("comment_captain.json");

		DanmuMessage.Comment expected = new
		(
			new DanmuSender(10000005, "用户7", GuardLevel.Captain, new DanmuMedal(24, "Knt", 1725515, 10000006, 25383355, true)),
			"？",
			4,
			25,
			14893055,
			DateTimeOffset.FromUnixTimeMilliseconds(1795344548119),
			"5e7c63ca70d385b033f842f35a6b02c84761",
			"7cc87912",
			DanmuCommentKind.Text,
			false
		);

		await Sequences.AssertEqualAsync(messages, [Received(expected)]);
	}

	[Test]
	public async Task EmoticonCommentCarriesItsNameAsTextAsync()
	{
		List<DanmuMessage> messages = ParseFixture("comment_emoticon.json");

		DanmuMessage.Comment expected = new
		(
			new DanmuSender(0, "测4***", GuardLevel.None, null),
			"哇哈哈",
			1,
			25,
			16777215,
			DateTimeOffset.FromUnixTimeMilliseconds(1795344457091),
			"091978355b6b306e16221aa46c6b02c81233",
			"d665fa75",
			DanmuCommentKind.Emoticon,
			false
		);

		await Sequences.AssertEqualAsync(messages, [Received(expected)]);
	}

	[Test]
	public async Task StormCommentIsMarkedAsync()
	{
		string json = Modify("comment_masked.json", static root => root["info"]![0]![9] = 2);

		await Assert.That(Parse(json, DanmuParseMode.Chat) is DanmuMessage.Comment { IsStorm: true }).IsTrue();
	}

	[Test]
	public async Task CommentFallsBackToTheNameInExtraAsync()
	{
		string json = Modify("comment_captain.json", static root => root["info"]![2]![1] = "");

		await Assert.That(Parse(json, DanmuParseMode.Chat) is DanmuMessage.Comment { Sender.Name: "用户7" }).IsTrue();
	}

	[Test]
	public async Task CommentWithMissingFieldsKeepsDefaultsAsync()
	{
		DanmuMessage? message = Parse("""{"cmd":"DANMU_MSG","info":[[0,"1",null,{}],"hi",{},"medal"]}""", DanmuParseMode.Chat);

		DanmuMessage.Comment expected = new(new DanmuSender(0, "", GuardLevel.None, null), "hi", 1, 0, 0, ReceivedAt.UtcTime, "", "", DanmuCommentKind.Text, false);

		await Assert.That(message).IsEqualTo(Received(expected));
	}

	[Test]
	public async Task CommentWithMalformedExtraHasNoIdAsync()
	{
		string json = Modify("comment_masked.json", static root => root["info"]![0]![15]!["extra"] = "{not json");

		await Assert.That(Parse(json, DanmuParseMode.Chat) is DanmuMessage.Comment { Id: "", Text: "666" }).IsTrue();
	}

	[Test]
	[Arguments("""{"cmd":"DANMU_MSG"}""")]
	[Arguments("""{"cmd":"DANMU_MSG","info":{}}""")]
	[Arguments("""{"cmd":"SUPER_CHAT_MESSAGE","data":[]}""")]
	[Arguments("""{"cmd":"SEND_GIFT"}""")]
	[Arguments("""{"cmd":"SEND_GIFT_V2","data":{}}""")]
	[Arguments("""{"cmd":"USER_TOAST_MSG_V2","data":"x"}""")]
	[Arguments("""{"cmd":"SUPER_CHAT_MESSAGE_DELETE","data":{}}""")]
	[Arguments("""{"cmd":"SUPER_CHAT_MESSAGE_DELETE","data":{"ids":[]}}""")]
	[Arguments("""{"cmd":"SUPER_CHAT_MESSAGE_DELETE","data":{"ids":[0,"x"]}}""")]
	public async Task ChatWithoutItsPayloadIsDroppedAsync(string json)
	{
		await Assert.That(Parse(json, DanmuParseMode.Chat)).IsNull();
	}

	[Test]
	public async Task SuperChatIsParsedAsync()
	{
		List<DanmuMessage> messages = ParseFixture("super_chat.json");

		DanmuMessage.SuperChat expected = new
		(
			20541912,
			new DanmuSender(10000007, "用户9", GuardLevel.Captain, new DanmuMedal(26, "老陈皮", 398668, 10000008, 719085, true)),
			"测试醒目留言",
			30_000,
			TimeSpan.FromSeconds(60),
			DateTimeOffset.FromUnixTimeSeconds(1795344559)
		);

		await Sequences.AssertEqualAsync(messages, [Received(expected)]);
	}

	[Test]
	[Arguments("0.5", 500L)]
	[Arguments("\"12.3\"", 12_300L)]
	[Arguments("-1", 0L)]
	[Arguments("1e20", 9_223_372_036_854_775_000L)]
	public async Task SuperChatPriceIsConvertedToThousandthsOfAYuanAsync(string price, long expected)
	{
		DanmuMessage? message = Parse($$$"""{"cmd":"SUPER_CHAT_MESSAGE","data":{"price":{{{price}}}}}""", DanmuParseMode.Chat);

		await Assert.That((message as DanmuMessage.SuperChat)?.Price).IsEqualTo(expected);
	}

	[Test]
	public async Task GiftV2IsParsedAsync()
	{
		List<DanmuMessage> messages = ParseFixture("gift_v2.json");

		DanmuMessage.Gift expected = new
		(
			new DanmuSender(10000002, "用户5", GuardLevel.None, new DanmuMedal(19, "瑪奇瑪", 13081892, 10000003, 0, true)),
			35339,
			"为国创打call",
			1,
			100,
			100,
			DanmuCoinType.Gold,
			DateTimeOffset.FromUnixTimeSeconds(1795344574)
		);

		using (Assert.Multiple())
		{
			await Sequences.AssertEqualAsync(messages, [Received(expected)]);
			await Assert.That(expected.PaidPrice).IsEqualTo(100);
		}
	}

	[Test]
	public async Task MaskedGiftV2WithoutMedalIsParsedAsync()
	{
		List<DanmuMessage> messages = ParseFixture("gift_v2_masked.json");

		DanmuMessage.Gift expected = new(new DanmuSender(0, "测3***", GuardLevel.None, null), 31164, "粉丝团灯牌", 1, 100, 100, DanmuCoinType.Gold, DateTimeOffset.FromUnixTimeSeconds(1795344444));

		await Sequences.AssertEqualAsync(messages, [Received(expected)]);
	}

	[Test]
	public async Task GiftV2ListingSeveralGiftsIsSplitAsync()
	{
		// Made from gift_v2_masked.json by appending two gifts, no captured message listed more than one
		List<DanmuMessage> messages = ParseFixture("gift_v2_multiple.json");

		DanmuSender sender = new(0, "测3***", GuardLevel.None, null);
		DateTimeOffset time = DateTimeOffset.FromUnixTimeSeconds(1795344444);
		DanmuMessage.Gift silver = new(sender, 1, "辣条", 3, 100, 300, DanmuCoinType.Silver, time);

		using (Assert.Multiple())
		{
			await Sequences.AssertEqualAsync(messages,
			[
				Received(new DanmuMessage.Gift(sender, 31164, "粉丝团灯牌", 1, 100, 100, DanmuCoinType.Gold, time)),
				Received(new DanmuMessage.Gift(sender, 31036, "小花花", 5, 100, 500, DanmuCoinType.Gold, time)),
				Received(silver)
			]);
			await Assert.That(silver.PaidPrice).IsEqualTo(0);
		}
	}

	[Test]
	public async Task MalformedGiftProtobufDropsTheWholeMessageAsync()
	{
		JsonNode root = JsonNode.Parse(Fixture("gift_v2_multiple.json")) ?? throw new InvalidDataException();
		byte[] pb = Convert.FromBase64String(root["data"]!["pb"]!.GetValue<string>());
		// The first two gifts stay intact, only the last one is cut off
		root["data"]!["pb"] = Convert.ToBase64String(pb.AsSpan(0, pb.Length - 3));
		List<DanmuMessage> output = [];

		using (Assert.Multiple())
		{
			await Assert.That(() => DanmuMessageParser.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()), DanmuParseMode.Chat, ReceivedAt, output)).Throws<InvalidDataException>();
			await Assert.That(output).IsEmpty();
		}
	}

	[Test]
	public async Task GiftIsParsedAsync()
	{
		// Hand-made from the documented fields, no SEND_GIFT arrived during the captures
		List<DanmuMessage> messages = ParseFixture("send_gift.json");

		DanmuMessage.Gift expected = new
		(
			new DanmuSender(10000010, "用户12", GuardLevel.Admiral, new DanmuMedal(21, "测试牌", 398668, 10000011, 1939021227, true)),
			1,
			"辣条",
			5,
			100,
			500,
			DanmuCoinType.Silver,
			DateTimeOffset.FromUnixTimeSeconds(1795344600)
		);

		await Sequences.AssertEqualAsync(messages, [Received(expected)]);
	}

	[Test]
	public async Task GiftWithoutTotalUsesUnitPriceTimesCountAsync()
	{
		DanmuMessage? message = Parse("""{"cmd":"SEND_GIFT","data":{"giftName":"x","num":4,"price":100,"coin_type":"gold"}}""", DanmuParseMode.Chat);

		await Assert.That(message is DanmuMessage.Gift { TotalPrice: 400, PaidPrice: 400, Time: var time } && time == ReceivedAt.UtcTime).IsTrue();
	}

	[Test]
	public async Task SuperChatDeleteIsParsedAsync()
	{
		// Hand-made from the fields the web page reads, no deletion arrived during the captures
		List<DanmuMessage> messages = ParseFixture("super_chat_delete.json");

		using (Assert.Multiple())
		{
			await Assert.That(messages.Count).IsEqualTo(1);
			await Assert.That(messages[0] is DanmuMessage.SuperChatDeleted { Ids: [20541912, 20541913] } deleted && deleted.ReceivedAt == ReceivedAt).IsTrue();
		}
	}

	[Test]
	public async Task SuperChatDeleteSkipsIdsThatAreNotPositiveAsync()
	{
		DanmuMessage? message = Parse("""{"cmd":"SUPER_CHAT_MESSAGE_DELETE","data":{"ids":[0,"42",-1,7]}}""", DanmuParseMode.Chat);

		await Assert.That(message is DanmuMessage.SuperChatDeleted { Ids: [42, 7] }).IsTrue();
	}

	[Test]
	public async Task GuardBuyIsIgnoredAsync()
	{
		// The same purchase also arrives as USER_TOAST_MSG_V2, parsing both would record it twice
		await Assert.That(ParseFixture("guard_buy.json")).IsEmpty();
	}

	[Test]
	public async Task UserToastIsParsedAsync()
	{
		List<DanmuMessage> messages = ParseFixture("user_toast_v2.json");

		DanmuMessage.GuardBought expected = new(new DanmuSender(10000012, "用户14", GuardLevel.Captain, null), GuardLevel.Captain, 1, 138000, false, DateTimeOffset.FromUnixTimeSeconds(1795350125));

		await Sequences.AssertEqualAsync(messages, [Received(expected)]);
	}

	[Test]
	public async Task UserToastRenewalIsParsedAsync()
	{
		List<DanmuMessage> messages = ParseFixture("user_toast_v2_renewal.json");

		DanmuMessage.GuardBought expected = new(new DanmuSender(10000014, "用户16", GuardLevel.Captain, null), GuardLevel.Captain, 1, 138000, true, DateTimeOffset.FromUnixTimeSeconds(1795350193));

		await Sequences.AssertEqualAsync(messages, [Received(expected)]);
	}

	[Test]
	[Arguments(0, false)]
	[Arguments(1, false)]
	[Arguments(2, true)]
	[Arguments(3, true)]
	public async Task UserToastOperationOtherThanOpeningIsRenewalAsync(int operation, bool isRenewal)
	{
		string json = Modify("user_toast_v2.json", root => root["data"]!["guard_info"]!["op_type"] = operation);

		await Assert.That(Parse(json, DanmuParseMode.Chat) is DanmuMessage.GuardBought guard && guard.IsRenewal == isRenewal).IsTrue();
	}

	[Test]
	[Arguments(0, true)]
	[Arguments(1, true)]
	[Arguments(2, false)]
	public async Task UserToastRepeatedFromSourceTwoIsDroppedAsync(int source, bool parsed)
	{
		string json = Modify("user_toast_v2.json", root => root["data"]!["option"]!["source"] = source);

		await Assert.That(Parse(json, DanmuParseMode.Chat) is DanmuMessage.GuardBought).IsEqualTo(parsed);
	}

	[Test]
	[Arguments("月", 3, 3, 414000L)]
	[Arguments("", 3, 3, 414000L)]
	[Arguments("天", 3, 1, 414000L)]
	[Arguments("月", 0, 1, 138000L)]
	public async Task UserToastPriceIsPerUnitAsync(string unit, int num, int months, long price)
	{
		string json = Modify("user_toast_v2.json", root =>
		{
			JsonNode pay = root["data"]!["pay_info"]!;
			pay["unit"] = unit;
			pay["num"] = num;
		});

		await Assert.That(Parse(json, DanmuParseMode.Chat) is DanmuMessage.GuardBought guard && guard.Count == months && guard.Price == price).IsTrue();
	}

	[Test]
	public async Task RawPrecedesTheParsedMessageAsync()
	{
		byte[] json = Fixture("user_toast_v2.json");

		List<DanmuMessage> messages = Parse(json, DanmuParseMode.Chat | DanmuParseMode.Raw);

		using (Assert.Multiple())
		{
			await Assert.That(messages.Count).IsEqualTo(2);
			await Assert.That(messages[0] is DanmuMessage.Raw { Command: "USER_TOAST_MSG_V2" } raw && raw.Json.Span.SequenceEqual(json) && raw.ReceivedAt == ReceivedAt).IsTrue();
			await Assert.That(messages[1]).IsTypeOf<DanmuMessage.GuardBought>();
		}
	}

	[Test]
	[Arguments("""{"cmd":"DANMU_MSG:4:0:2:2:2:0","info":[]}""", "DANMU_MSG")]
	[Arguments("""{"cmd":"WATCHED_CHANGE","data":{"num":1}}""", "WATCHED_CHANGE")]
	[Arguments("""{"data":{},"cmd":"LIVE"}""", "LIVE")]
	public async Task RawIsYieldedForEveryCommandAsync(string json, string command)
	{
		List<DanmuMessage> messages = Parse(Encoding.UTF8.GetBytes(json), DanmuParseMode.Raw);

		await Assert.That(messages.FirstOrDefault() is DanmuMessage.Raw raw && raw.Command == command && Encoding.UTF8.GetString(raw.Json.Span) == json).IsTrue();
	}

	[Test]
	public async Task RawDoesNotShareTheReceiveBufferAsync()
	{
		byte[] json = Encoding.UTF8.GetBytes("""{"cmd":"A"}""");
		DanmuMessage.Raw? raw = Parse(json, DanmuParseMode.Raw).SingleOrDefault() as DanmuMessage.Raw;
		json[2] = (byte)'X';

		await Assert.That(raw is not null && Encoding.UTF8.GetString(raw.Json.Span) == """{"cmd":"A"}""").IsTrue();
	}

	[Test]
	[Arguments("""{"data":{}}""")]
	[Arguments("""[1,2]""")]
	[Arguments("""{"cmd":1}""")]
	public async Task RawSkipsBodiesWithoutCommandAsync(string json)
	{
		await Assert.That(Parse(Encoding.UTF8.GetBytes(json), DanmuParseMode.Raw)).IsEmpty();
	}

	[Test]
	[Arguments("""{"cmd":"A","x":""")]
	[Arguments("""{"cmd":"A"} {}""")]
	[Arguments("""{"cmd":"A"}x""")]
	[Arguments("")]
	public async Task RawRejectsBodiesThatAreNotValidJsonAsync(string json)
	{
		List<DanmuMessage> output = [];

		using (Assert.Multiple())
		{
			await Assert.That(() => DanmuMessageParser.Parse(Encoding.UTF8.GetBytes(json), DanmuParseMode.Raw, ReceivedAt, output)).Throws<JsonException>();
			await Assert.That(output).IsEmpty();
		}
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
