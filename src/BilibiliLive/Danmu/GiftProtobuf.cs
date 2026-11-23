namespace BilibiliLive.Danmu;

// SEND_GIFT_V2 的 data.pb；匿名连接收不到 uid 字段，proto3 也会省略零值
internal static class GiftProtobuf
{
	private const int UserIdField = 1;
	private const int UserNameField = 2;
	private const int GuardLevelField = 5;
	private const int MedalField = 8;
	private const int GiftField = 10;

	private const int MedalLevelField = 5;
	private const int MedalNameField = 6;
	private const int MedalColorField = 7;
	private const int MedalIsLitField = 11;

	private const int GiftNameField = 2;
	private const int GiftCountField = 3;
	private const int GiftPriceField = 5;
	private const int GiftTotalPriceField = 7;
	private const int GiftCoinTypeField = 8;

	public static void Parse(ReadOnlySpan<byte> data, DanmuReceiveTime receivedAt, List<DanmuMessage> output)
	{
		DanmuSender sender = ReadSender(data);
		ProtobufReader reader = new(data);

		// 线上礼物排在发送者后面，但 protobuf 允许任意字段顺序，所以发送者单独遍历一遍来读
		while (reader.TryReadTag(out int field, out ProtobufWireType wireType))
		{
			if (field is GiftField && wireType is ProtobufWireType.LengthDelimited)
			{
				output.Add(ReadGift(reader.ReadLengthDelimited(), sender, receivedAt));
			}
			else
			{
				reader.Skip(wireType);
			}
		}
	}

	private static DanmuSender ReadSender(ReadOnlySpan<byte> data)
	{
		ProtobufReader reader = new(data);
		long userId = 0;
		string name = string.Empty;
		GuardLevel guardLevel = GuardLevel.None;
		DanmuMedal? medal = null;

		while (reader.TryReadTag(out int field, out ProtobufWireType wireType))
		{
			switch (field, wireType)
			{
				case (UserIdField, ProtobufWireType.Varint):
				{
					userId = reader.ReadInt64();
					break;
				}
				case (UserNameField, ProtobufWireType.LengthDelimited):
				{
					name = reader.ReadString();
					break;
				}
				case (GuardLevelField, ProtobufWireType.Varint):
				{
					guardLevel = (GuardLevel)reader.ReadInt32();
					break;
				}
				case (MedalField, ProtobufWireType.LengthDelimited):
				{
					medal = ReadMedal(reader.ReadLengthDelimited());
					break;
				}
				default:
				{
					reader.Skip(wireType);
					break;
				}
			}
		}

		return new DanmuSender(userId, name, guardLevel, medal);
	}

	private static DanmuMedal? ReadMedal(ReadOnlySpan<byte> data)
	{
		ProtobufReader reader = new(data);
		int level = 0;
		string name = string.Empty;
		int color = 0;
		bool isLit = false;

		while (reader.TryReadTag(out int field, out ProtobufWireType wireType))
		{
			switch (field, wireType)
			{
				case (MedalLevelField, ProtobufWireType.Varint):
				{
					level = reader.ReadInt32();
					break;
				}
				case (MedalNameField, ProtobufWireType.LengthDelimited):
				{
					name = reader.ReadString();
					break;
				}
				case (MedalColorField, ProtobufWireType.Varint):
				{
					color = reader.ReadInt32();
					break;
				}
				case (MedalIsLitField, ProtobufWireType.Varint):
				{
					isLit = reader.ReadVarint() is not 0;
					break;
				}
				default:
				{
					reader.Skip(wireType);
					break;
				}
			}
		}

		// 没有粉丝牌的用户收到的是空消息
		return name.Length is 0 ? null : new DanmuMedal(level, name, color, isLit);
	}

	private static DanmuMessage.Gift ReadGift(ReadOnlySpan<byte> data, DanmuSender sender, DanmuReceiveTime receivedAt)
	{
		ProtobufReader reader = new(data);
		string giftName = string.Empty;
		int count = 0;
		long unitPrice = 0;
		long totalPrice = 0;
		DanmuCoinType coinType = DanmuCoinType.Silver;

		while (reader.TryReadTag(out int field, out ProtobufWireType wireType))
		{
			switch (field, wireType)
			{
				case (GiftNameField, ProtobufWireType.LengthDelimited):
				{
					giftName = reader.ReadString();
					break;
				}
				case (GiftCountField, ProtobufWireType.Varint):
				{
					count = reader.ReadInt32();
					break;
				}
				case (GiftPriceField, ProtobufWireType.Varint):
				{
					unitPrice = reader.ReadInt64();
					break;
				}
				case (GiftTotalPriceField, ProtobufWireType.Varint):
				{
					totalPrice = reader.ReadInt64();
					break;
				}
				case (GiftCoinTypeField, ProtobufWireType.LengthDelimited):
				{
					coinType = DanmuMessageParser.ParseCoinType(reader.ReadString());
					break;
				}
				default:
				{
					reader.Skip(wireType);
					break;
				}
			}
		}

		return new DanmuMessage.Gift(sender, giftName, count, DanmuMessageParser.GiftTotalPrice(totalPrice, unitPrice, count), coinType)
		{
			ReceivedAt = receivedAt
		};
	}
}
