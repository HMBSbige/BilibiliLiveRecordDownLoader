using BilibiliLive.Danmu;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BilibiliLive.Tests.Danmu;

public class DanmuMessageParserTest
{
	private static readonly DanmuReceiveTime ReceivedAt = new(42, new DateTimeOffset(2026, 11, 22, 10, 0, 0, TimeSpan.Zero));

	private static readonly DanmuMessage.Comment MaskedComment = new
	(
		new DanmuSender(0, "测1***", GuardLevel.None, new DanmuMedal(18, "i奶芙", 13081892, 10000001, 1939021227, true)),
		"666",
		1,
		25,
		16777215,
		DateTimeOffset.FromUnixTimeMilliseconds(1795344443482),
		"d3bbbee6",
		DanmuCommentKind.Text,
		false
	);

	private static readonly DanmuMessage.Comment CaptainComment = new
	(
		new DanmuSender(10000005, "用户7", GuardLevel.Captain, new DanmuMedal(24, "Knt", 1725515, 10000006, 25383355, true)),
		"？",
		4,
		25,
		14893055,
		DateTimeOffset.FromUnixTimeMilliseconds(1795344548119),
		"7cc87912",
		DanmuCommentKind.Text,
		false
	);

	private static readonly DanmuMessage.Comment EmoticonComment = new
	(
		new DanmuSender(0, "测4***", GuardLevel.None, null),
		"哇哈哈",
		1,
		25,
		16777215,
		DateTimeOffset.FromUnixTimeMilliseconds(1795344457091),
		"a4bc8e70",
		DanmuCommentKind.Emoticon,
		false
	);

	private static readonly DanmuMessage.SuperChat CapturedSuperChat = new
	(
		20541912,
		new DanmuSender(10000007, "用户9", GuardLevel.Captain, new DanmuMedal(26, "老陈皮", 398668, 10000008, 719085, true)),
		"测试醒目留言",
		30_000,
		TimeSpan.FromSeconds(60),
		DateTimeOffset.FromUnixTimeSeconds(1795344559)
	);

	private static readonly DanmuMessage.Gift GiftV2 = new
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

	private static readonly DanmuMessage.Gift MaskedGiftV2 = new(new DanmuSender(0, "测3***", GuardLevel.None, null), 31164, "粉丝团灯牌", 1, 100, 100, DanmuCoinType.Gold, DateTimeOffset.FromUnixTimeSeconds(1795344444));

	// Hand-made from the documented fields, no SEND_GIFT arrived during the captures
	private static readonly DanmuMessage.Gift SendGift = new
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

	private static readonly DanmuMessage.GuardBought UserToast = new(new DanmuSender(10000012, "用户14", GuardLevel.Captain, null), GuardLevel.Captain, 1, 138000, false, DateTimeOffset.FromUnixTimeSeconds(1795350125));

	private static readonly DanmuMessage.GuardBought UserToastRenewal = new(new DanmuSender(10000014, "用户16", GuardLevel.Captain, null), GuardLevel.Captain, 1, 138000, true, DateTimeOffset.FromUnixTimeSeconds(1795350193));

	public static IEnumerable<TestDataRow<Func<(string Fixture, DanmuMessage Expected)>>> Samples()
	{
		yield return new(static () => ("comment_masked.json", MaskedComment), DisplayName: "A masked comment is parsed");
		yield return new(static () => ("comment_captain.json", CaptainComment), DisplayName: "A comment of a guard member is parsed");
		yield return new(static () => ("comment_emoticon.json", EmoticonComment), DisplayName: "An emoticon comment carries its name as text");
		yield return new(static () => ("super_chat.json", CapturedSuperChat), DisplayName: "A super chat is parsed");
		yield return new(static () => ("gift_v2.json", GiftV2), DisplayName: "A protobuf gift is parsed");
		yield return new(static () => ("gift_v2_masked.json", MaskedGiftV2), DisplayName: "A masked protobuf gift without medal is parsed");
		yield return new(static () => ("send_gift.json", SendGift), DisplayName: "A JSON gift is parsed");
		yield return new(static () => ("user_toast_v2.json", UserToast), DisplayName: "A guard purchase is parsed");
		yield return new(static () => ("user_toast_v2_renewal.json", UserToastRenewal), DisplayName: "A guard renewal is parsed");
	}

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

	private static DanmuMessage? ParseFixture(string name, DanmuParseMode mode = DanmuParseMode.Chat)
	{
		return Parse(Fixture(name), mode).SingleOrDefault();
	}

	private static T Received<T>(T message) where T : DanmuMessage
	{
		return message with { ReceivedAt = ReceivedAt };
	}

	// Fails loudly when a fixture changes shape, instead of silently leaving it unmodified
	private static JsonNode Required(JsonNode? node)
	{
		return node ?? throw new InvalidDataException();
	}

	private static string Modify(string fixture, Action<JsonNode> modify)
	{
		JsonNode root = Required(JsonNode.Parse(Fixture(fixture)));
		modify(root);
		return root.ToJsonString();
	}

	private static string ModifyGiftProtobuf(string fixture, Func<byte[], byte[]> modify)
	{
		return Modify(fixture, root =>
		{
			JsonNode data = Required(root["data"]);
			data["pb"] = Convert.ToBase64String(modify(Convert.FromBase64String(Required(data["pb"]).GetValue<string>())));
		});
	}

	// The first two gifts stay intact, only the last one is cut off
	private static byte[] TruncatedGifts()
	{
		return Encoding.UTF8.GetBytes(ModifyGiftProtobuf("gift_v2_multiple.json", static pb => pb.AsSpan(0, pb.Length - 3).ToArray()));
	}

	private static string Text(ReadOnlyMemory<byte> json)
	{
		return Encoding.UTF8.GetString(json.Span);
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
		DanmuMessage? message = ParseFixture("log_in_notice.json", includeChat ? DanmuParseMode.Chat : DanmuParseMode.None);

		await Assert.That(message).IsEqualTo(Received(new DanmuMessage.LoginNotice("为保护用户隐私，未登录无法查看他人昵称")));
	}

	[Test]
	[Arguments("""{"cmd":"SUPER_CHAT_MESSAGE","data":{"price":30}}""")]
	[Arguments("""{"cmd":"SEND_GIFT_V2","data":{"pb":"not base64"}}""")]
	[Arguments("""{"cmd":"USER_TOAST_MSG_V2","data":{}}""")]
	[Arguments("""{"cmd":"SUPER_CHAT_MESSAGE_DELETE","data":{"ids":[1]}}""")]
	// Malformed after the cmd, so reaching the parser would throw
	[Arguments("""{"cmd":"DANMU_MSG","info":[[0,1,""")]
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
		await Assert.That(() => Parse("""{"cmd":"LIVE""")).Throws<JsonException>();
	}

	[Test]
	[MethodDataSource(nameof(Samples))]
	public async Task SampleIsParsedAsync(string fixture, DanmuMessage expected)
	{
		await Assert.That(ParseFixture(fixture)).IsEqualTo(Received(expected));
	}

	[Test]
	public async Task StormCommentIsMarkedAsync()
	{
		string json = Modify("comment_masked.json", static root => Required(Required(root["info"])[0])[9] = 2);

		await Assert.That(Parse(json, DanmuParseMode.Chat)).IsEqualTo(Received(MaskedComment with { IsStorm = true }));
	}

	[Test]
	public async Task CommentFallsBackToTheNameInExtraAsync()
	{
		string json = Modify("comment_captain.json", static root => Required(Required(root["info"])[2])[1] = "");

		await Assert.That(Parse(json, DanmuParseMode.Chat)).IsEqualTo(Received(CaptainComment));
	}

	[Test]
	public async Task CommentWithMissingFieldsKeepsDefaultsAsync()
	{
		DanmuMessage? message = Parse("""{"cmd":"DANMU_MSG","info":[[0,"1",null,{}],"hi",{},"medal"]}""", DanmuParseMode.Chat);

		DanmuMessage.Comment expected = new(new DanmuSender(0, "", GuardLevel.None, null), "hi", 1, 0, 0, ReceivedAt.UtcTime, "", DanmuCommentKind.Text, false);

		await Assert.That(message).IsEqualTo(Received(expected));
	}

	[Test]
	public async Task CommentWithoutMillisecondsFallsBackToTheSecondsAsync()
	{
		string json = Modify("comment_masked.json", static root => Required(Required(root["info"])[0])[4] = 0);

		await Assert.That(Parse(json, DanmuParseMode.Chat)).IsEqualTo(Received(MaskedComment with { SentAt = DateTimeOffset.FromUnixTimeSeconds(1795344443) }));
	}

	[Test]
	public async Task LoneSurrogateThrowsInvalidOperationExceptionAsync()
	{
		await Assert.That(() => Parse("""{"cmd":"DANMU_MSG","info":[[0,1,25],"\ud83d"]}""", DanmuParseMode.Chat)).Throws<InvalidOperationException>();
	}

	[Test]
	[Arguments("""{"cmd":"DANMU_MSG"}""")]
	[Arguments("""{"cmd":"DANMU_MSG","info":{}}""")]
	[Arguments("""{"cmd":"SUPER_CHAT_MESSAGE","data":[]}""")]
	[Arguments("""{"cmd":"SEND_GIFT"}""")]
	[Arguments("""{"cmd":"SEND_GIFT_V2","data":{}}""")]
	[Arguments("""{"cmd":"USER_TOAST_MSG_V2","data":"x"}""")]
	[Arguments("""{"cmd":"SUPER_CHAT_MESSAGE_DELETE","data":{}}""")]
	[Arguments("""{"cmd":"SUPER_CHAT_MESSAGE_DELETE","data":{"ids":[0,"x"]}}""")]
	public async Task ChatWithoutItsPayloadIsDroppedAsync(string json)
	{
		await Assert.That(Parse(json, DanmuParseMode.Chat)).IsNull();
	}

	[Test]
	[Arguments("0.5", 500L)]
	[Arguments("\"12.3\"", 12_300L)]
	[Arguments("-1", 0L)]
	[Arguments("1e20", 9_223_372_036_854_775_000L)]
	public async Task SuperChatPriceIsConvertedToThousandthsOfAYuanAsync(string price, long expected)
	{
		DanmuMessage? message = Parse($$$"""{"cmd":"SUPER_CHAT_MESSAGE","data":{"price":{{{price}}}}}""", DanmuParseMode.Chat);

		await Assert.That(message).IsTypeOf<DanmuMessage.SuperChat>().And.Member(static superChat => superChat.Price, actual => actual.IsEqualTo(expected));
	}

	[Test]
	[Arguments("1e30")]
	[Arguments("1000000000000")]
	[Arguments("-5")]
	public async Task SuperChatDurationOutOfRangeIsUnknownAsync(string time)
	{
		DanmuMessage? message = Parse($$$"""{"cmd":"SUPER_CHAT_MESSAGE","data":{"price":30,"time":{{{time}}}}}""", DanmuParseMode.Chat);

		await Assert.That(message).IsTypeOf<DanmuMessage.SuperChat>().And.Member(static superChat => superChat.Duration, static duration => duration.IsEqualTo(TimeSpan.Zero));
	}

	[Test]
	public async Task GiftV2ListingSeveralGiftsIsSplitAsync()
	{
		// Made from gift_v2_masked.json by appending two gifts, no captured message listed more than one
		List<DanmuMessage> messages = Parse(Fixture("gift_v2_multiple.json"), DanmuParseMode.Chat);

		await Assert.That(messages).IsEquivalentTo(
		[
			Received(MaskedGiftV2),
			Received(MaskedGiftV2 with { GiftId = 31036, GiftName = "小花花", Count = 5, TotalPrice = 500 }),
			Received(MaskedGiftV2 with { GiftId = 1, GiftName = "辣条", Count = 3, TotalPrice = 300, CoinType = DanmuCoinType.Silver })
		], EqualityComparer<DanmuMessage>.Default, CollectionOrdering.Matching);
	}

	[Test]
	public async Task MalformedGiftProtobufDropsTheWholeMessageAsync()
	{
		List<DanmuMessage> output = [];

		using (Assert.Multiple())
		{
			await Assert.That(() => DanmuMessageParser.Parse(TruncatedGifts(), DanmuParseMode.Chat, ReceivedAt, output)).Throws<InvalidDataException>();
			await Assert.That(output).IsEmpty();
		}
	}

	[Test]
	public async Task MalformedGiftProtobufKeepsTheRawMessageAsync()
	{
		byte[] json = TruncatedGifts();
		List<DanmuMessage> output = [];

		await Assert.That(() => DanmuMessageParser.Parse(json, DanmuParseMode.Chat | DanmuParseMode.Raw, ReceivedAt, output)).Throws<InvalidDataException>();
		DanmuMessage message = await Assert.That(output).HasSingleItem();

		await Assert.That(message).IsTypeOf<DanmuMessage.Raw>()
			.And.Member(static raw => raw.Command, static command => command.IsEqualTo("SEND_GIFT_V2"))
			.And.Member(static raw => Text(raw.Json), text => text.IsEqualTo(Encoding.UTF8.GetString(json)));
	}

	[Test]
	public async Task GiftV2SenderIsReadWhereverItIsOnTheWireAsync()
	{
		string json = ModifyGiftProtobuf("gift_v2.json", MoveGiftsFirst);

		await Assert.That(Parse(json, DanmuParseMode.Chat)).IsEqualTo(Received(GiftV2));
	}

	private static byte[] MoveGiftsFirst(byte[] pb)
	{
		ProtobufReader reader = new(pb);
		List<(int Start, int End)> gifts = [];

		while (reader.TryReadTag(out int field, out ProtobufWireType wireType))
		{
			if (field is 10 && wireType is ProtobufWireType.LengthDelimited)
			{
				ReadOnlySpan<byte> payload = reader.ReadLengthDelimited();
				pb.AsSpan().Overlaps(payload, out int offset);
				// One tag byte and the length varint precede the payload
				gifts.Add((offset - 1 - VarintLength(payload.Length), offset + payload.Length));
			}
			else
			{
				reader.Skip(wireType);
			}
		}

		List<byte> moved = [];
		List<byte> rest = [];
		int position = 0;

		foreach ((int start, int end) in gifts)
		{
			rest.AddRange(pb.AsSpan(position, start - position));
			moved.AddRange(pb.AsSpan(start, end - start));
			position = end;
		}

		rest.AddRange(pb.AsSpan(position));
		return [.. moved, .. rest];

		static int VarintLength(int value)
		{
			int length = 1;

			while (value >= 0x80)
			{
				value >>= 7;
				++length;
			}

			return length;
		}
	}

	[Test]
	public async Task GiftWithoutTotalUsesUnitPriceTimesCountAsync()
	{
		DanmuMessage? message = Parse("""{"cmd":"SEND_GIFT","data":{"giftName":"x","num":4,"price":100,"coin_type":"gold"}}""", DanmuParseMode.Chat);

		DanmuMessage.Gift expected = new(new DanmuSender(0, "", GuardLevel.None, null), 0, "x", 4, 100, 400, DanmuCoinType.Gold, ReceivedAt.UtcTime);

		await Assert.That(message).IsEqualTo(Received(expected));
	}

	[Test]
	[Arguments(DanmuCoinType.Gold, 500L)]
	[Arguments(DanmuCoinType.Silver, 0L)]
	public async Task OnlyGoldGiftsArePaidAsync(DanmuCoinType coinType, long paidPrice)
	{
		await Assert.That((SendGift with { CoinType = coinType }).PaidPrice).IsEqualTo(paidPrice);
	}

	[Test]
	public async Task SuperChatDeleteIsParsedAsync()
	{
		// Hand-made from the fields the web page reads, no deletion arrived during the captures
		await Assert.That(ParseFixture("super_chat_delete.json")).IsTypeOf<DanmuMessage.SuperChatDeleted>()
			.And.Member(static deleted => deleted.Ids, static ids => ids.IsEquivalentTo([20541912L, 20541913], CollectionOrdering.Matching))
			.And.Member(static deleted => deleted.ReceivedAt, static time => time.IsEqualTo(ReceivedAt));
	}

	[Test]
	public async Task SuperChatDeleteSkipsIdsThatAreNotPositiveAsync()
	{
		DanmuMessage? message = Parse("""{"cmd":"SUPER_CHAT_MESSAGE_DELETE","data":{"ids":[0,"42",-1,7]}}""", DanmuParseMode.Chat);

		await Assert.That(message).IsTypeOf<DanmuMessage.SuperChatDeleted>().And.Member(static deleted => deleted.Ids, static ids => ids.IsEquivalentTo([42L, 7], CollectionOrdering.Matching));
	}

	[Test]
	public async Task GuardBuyIsIgnoredAsync()
	{
		// The same purchase also arrives as USER_TOAST_MSG_V2, parsing both would record it twice
		await Assert.That(Parse(Fixture("guard_buy.json"), DanmuParseMode.Chat)).IsEmpty();
	}

	[Test]
	[Arguments(0, false)]
	[Arguments(3, true)]
	public async Task UserToastOperationOtherThanOpeningIsRenewalAsync(int operation, bool isRenewal)
	{
		string json = Modify("user_toast_v2.json", root => Required(Required(root["data"])["guard_info"])["op_type"] = operation);

		await Assert.That(Parse(json, DanmuParseMode.Chat)).IsEqualTo(Received(UserToast with { IsRenewal = isRenewal }));
	}

	[Test]
	[Arguments(1, true)]
	[Arguments(2, false)]
	public async Task UserToastRepeatedFromSourceTwoIsDroppedAsync(int source, bool parsed)
	{
		string json = Modify("user_toast_v2.json", root => Required(Required(root["data"])["option"])["source"] = source);

		await Assert.That(Parse(json, DanmuParseMode.Chat)).IsEqualTo(parsed ? Received(UserToast) : null);
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
			JsonNode pay = Required(Required(root["data"])["pay_info"]);
			pay["unit"] = unit;
			pay["num"] = num;
		});

		await Assert.That(Parse(json, DanmuParseMode.Chat)).IsEqualTo(Received(UserToast with { Count = months, Price = price }));
	}

	[Test]
	public async Task RawPrecedesTheParsedMessageAsync()
	{
		byte[] json = Fixture("user_toast_v2.json");

		List<DanmuMessage> messages = Parse(json, DanmuParseMode.Chat | DanmuParseMode.Raw);

		await Assert.That(messages).Count().IsEqualTo(2);

		using (Assert.Multiple())
		{
			await Assert.That(messages[0]).IsTypeOf<DanmuMessage.Raw>()
				.And.Member(static raw => raw.Command, static command => command.IsEqualTo("USER_TOAST_MSG_V2"))
				.And.Member(static raw => Text(raw.Json), text => text.IsEqualTo(Encoding.UTF8.GetString(json)))
				.And.Member(static raw => raw.ReceivedAt, static time => time.IsEqualTo(ReceivedAt));
			await Assert.That(messages[1]).IsEqualTo(Received(UserToast));
		}
	}

	[Test]
	[Arguments("""{"cmd":"DANMU_MSG:4:0:2:2:2:0","info":[]}""", "DANMU_MSG")]
	[Arguments("""{"cmd":"WATCHED_CHANGE","data":{"num":1}}""", "WATCHED_CHANGE")]
	[Arguments("""{"data":{},"cmd":"LIVE"}""", "LIVE")]
	public async Task RawIsYieldedForEveryCommandAsync(string json, string command)
	{
		List<DanmuMessage> messages = Parse(Encoding.UTF8.GetBytes(json), DanmuParseMode.Raw);

		await Assert.That(messages.FirstOrDefault()).IsTypeOf<DanmuMessage.Raw>()
			.And.Member(static raw => raw.Command, actual => actual.IsEqualTo(command))
			.And.Member(static raw => Text(raw.Json), text => text.IsEqualTo(json));
	}

	[Test]
	public async Task RawDoesNotShareTheReceiveBufferAsync()
	{
		byte[] json = Encoding.UTF8.GetBytes("""{"cmd":"A"}""");
		DanmuMessage? message = Parse(json, DanmuParseMode.Raw).SingleOrDefault();
		json[2] = (byte)'X';

		await Assert.That(message).IsTypeOf<DanmuMessage.Raw>().And.Member(static raw => Text(raw.Json), static text => text.IsEqualTo("""{"cmd":"A"}"""));
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
	[Arguments("""[0]""")]
	[Arguments("""not json""")]
	[Arguments("""{"code":null}""")]
	[Arguments("""{"code":"0"}""")]
	public async Task AuthReplyWithoutCodeIsNullAsync(string json)
	{
		await Assert.That(DanmuMessageParser.GetAuthReplyCode(Encoding.UTF8.GetBytes(json))).IsNull();
	}
}
