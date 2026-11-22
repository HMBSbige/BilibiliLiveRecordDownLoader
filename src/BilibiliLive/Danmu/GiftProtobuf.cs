namespace BilibiliLive.Danmu;

// The data.pb of SEND_GIFT_V2; an anonymous connection receives no uid field, proto3 leaves out zero values
internal static class GiftProtobuf
{
	private const int UserIdField = 1;
	private const int UserNameField = 2;
	private const int GuardLevelField = 5;
	private const int MedalField = 8;
	private const int GiftField = 10;

	private const int MedalStreamerUserIdField = 1;
	private const int MedalLevelField = 5;
	private const int MedalNameField = 6;
	private const int MedalColorField = 7;
	private const int MedalIsLitField = 11;

	private const int GiftIdField = 1;
	private const int GiftNameField = 2;
	private const int GiftCountField = 3;
	private const int GiftPriceField = 5;
	private const int GiftTotalPriceField = 7;
	private const int GiftCoinTypeField = 8;
	private const int GiftTimestampField = 10;

	public static void Parse(ReadOnlySpan<byte> data, DanmuReceiveTime receivedAt, List<DanmuMessage> output)
	{
		DanmuSender sender = ReadSender(data);
		ProtobufReader reader = new(data);

		// Gifts come after the sender on the wire, but protobuf allows any field order, so the sender is read in a pass of its own
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
			switch ((field, wireType))
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
		long streamerUserId = 0;
		int level = 0;
		string name = string.Empty;
		int color = 0;
		bool isLit = false;

		while (reader.TryReadTag(out int field, out ProtobufWireType wireType))
		{
			switch ((field, wireType))
			{
				case (MedalStreamerUserIdField, ProtobufWireType.Varint):
				{
					streamerUserId = reader.ReadInt64();
					break;
				}
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

		// Users without a medal get an empty message
		return name.Length is 0 ? null : new DanmuMedal(level, name, color, streamerUserId, 0, isLit);
	}

	private static DanmuMessage.Gift ReadGift(ReadOnlySpan<byte> data, DanmuSender sender, DanmuReceiveTime receivedAt)
	{
		ProtobufReader reader = new(data);
		long giftId = 0;
		string giftName = string.Empty;
		int count = 0;
		long unitPrice = 0;
		long totalPrice = 0;
		DanmuCoinType coinType = DanmuCoinType.Silver;
		long timestamp = 0;

		while (reader.TryReadTag(out int field, out ProtobufWireType wireType))
		{
			switch ((field, wireType))
			{
				case (GiftIdField, ProtobufWireType.Varint):
				{
					giftId = reader.ReadInt64();
					break;
				}
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
				case (GiftTimestampField, ProtobufWireType.Varint):
				{
					timestamp = reader.ReadInt64();
					break;
				}
				default:
				{
					reader.Skip(wireType);
					break;
				}
			}
		}

		return new DanmuMessage.Gift(sender, giftId, giftName, count, unitPrice, totalPrice > 0 ? totalPrice : unitPrice * count, coinType, DanmuMessageParser.FromUnixSeconds(timestamp, receivedAt))
		{
			ReceivedAt = receivedAt
		};
	}
}
