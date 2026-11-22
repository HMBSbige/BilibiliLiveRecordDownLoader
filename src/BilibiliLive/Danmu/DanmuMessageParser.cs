using System.Globalization;
using System.Text.Json;

namespace BilibiliLive.Danmu;

// Fields go missing or change type between server versions, so every read falls back to a default instead of throwing
internal static class DanmuMessageParser
{
	private static readonly long MaxUnixSeconds = DateTimeOffset.MaxValue.ToUnixTimeSeconds();

	/// <summary>Appends the messages a command packet carries to <paramref name="output"/>; when it throws, only the raw message may have been appended.</summary>
	/// <exception cref="JsonException">The body is not valid JSON.</exception>
	/// <exception cref="InvalidDataException">The protobuf of a gift is malformed.</exception>
	/// <exception cref="FormatException">The base64 of a gift is malformed.</exception>
	public static void Parse(ReadOnlyMemory<byte> json, DanmuParseMode mode, DanmuReceiveTime receivedAt, List<DanmuMessage> output)
	{
		string? command;

		if (mode.HasFlag(DanmuParseMode.Raw))
		{
			// A raw message ends up verbatim in a JSON Lines file, so it has to be valid as a whole, not just up to its cmd
			command = ValidateAndGetCommand(json.Span);

			if (command is not null)
			{
				output.Add(new DanmuMessage.Raw(command, json.ToArray()) { ReceivedAt = receivedAt });
			}
		}
		else
		{
			// Most traffic is chat nobody asked for, so it is rejected after reading only the cmd
			command = GetCommand(json.Span);
		}

		int count = output.Count;

		try
		{
			ParseCommand(command, json, mode.HasFlag(DanmuParseMode.Chat), receivedAt, output);
		}
		catch
		{
			// A gift message split into several gifts is dropped as a whole
			output.RemoveRange(count, output.Count - count);
			throw;
		}
	}

	private static void ParseCommand(string? command, ReadOnlyMemory<byte> json, bool includeChat, DanmuReceiveTime receivedAt, List<DanmuMessage> output)
	{
		switch (command)
		{
			case "LIVE":
			{
				output.Add(new DanmuMessage.LiveStarted { ReceivedAt = receivedAt });
				break;
			}
			case "PREPARING":
			{
				output.Add(ParsePreparing(json) with { ReceivedAt = receivedAt });
				break;
			}
			case "ROOM_CHANGE" when ParseRoomChange(json) is { } changed:
			{
				output.Add(changed with { ReceivedAt = receivedAt });
				break;
			}
			// Arrives once right after authentication, so later subscribers still learn whether nicknames are masked
			case "LOG_IN_NOTICE":
			{
				output.Add(ParseLoginNotice(json) with { ReceivedAt = receivedAt });
				break;
			}
			case "DANMU_MSG" when includeChat && ParseComment(json, receivedAt) is { } comment:
			{
				output.Add(comment);
				break;
			}
			case "SUPER_CHAT_MESSAGE" when includeChat && ParseSuperChat(json, receivedAt) is { } superChat:
			{
				output.Add(superChat);
				break;
			}
			case "SEND_GIFT" when includeChat && ParseGift(json, receivedAt) is { } gift:
			{
				output.Add(gift);
				break;
			}
			case "SEND_GIFT_V2" when includeChat:
			{
				ParseGiftV2(json, receivedAt, output);
				break;
			}
			case "SUPER_CHAT_MESSAGE_DELETE" when includeChat && ParseSuperChatDelete(json) is { } deleted:
			{
				output.Add(deleted with { ReceivedAt = receivedAt });
				break;
			}
			// Every purchase also arrives as GUARD_BUY, but that only carries the list price; the web page shows purchases from this one
			case "USER_TOAST_MSG_V2" when includeChat && ParseUserToast(json, receivedAt) is { } guard:
			{
				output.Add(guard);
				break;
			}
		}
	}

	/// <returns>The <c>code</c> of an AuthReply body, or <see langword="null"/> when the body has none.</returns>
	public static long? GetAuthReplyCode(ReadOnlyMemory<byte> json)
	{
		try
		{
			using JsonDocument document = JsonDocument.Parse(json);
			return document.RootElement is { ValueKind: JsonValueKind.Object } root && root.TryGetProperty("code", out JsonElement code) && code.TryGetInt64(out long value) ? value : null;
		}
		catch (JsonException)
		{
			return null;
		}
	}

	internal static string? GetCommand(ReadOnlySpan<byte> json)
	{
		Utf8JsonReader reader = new(json);

		if (!reader.Read() || reader.TokenType is not JsonTokenType.StartObject)
		{
			return null;
		}

		while (reader.Read() && reader.TokenType is JsonTokenType.PropertyName)
		{
			bool isCommand = reader.ValueTextEquals("cmd"u8);

			if (!reader.Read())
			{
				return null;
			}

			if (isCommand)
			{
				return reader.TokenType is JsonTokenType.String ? TrimCommand(reader.GetString()) : null;
			}

			reader.Skip();
		}

		return null;
	}

	/// <summary>Reads the whole body, so it throws on JSON that <see cref="GetCommand"/> would accept because the error lies after the cmd.</summary>
	internal static string? ValidateAndGetCommand(ReadOnlySpan<byte> json)
	{
		Utf8JsonReader reader = new(json);
		string? command = null;

		if (!reader.Read())
		{
			throw new JsonException("Empty danmu message.");
		}

		if (reader.TokenType is not JsonTokenType.StartObject)
		{
			reader.Skip();
		}
		else
		{
			while (reader.Read() && reader.TokenType is JsonTokenType.PropertyName)
			{
				bool isCommand = command is null && reader.ValueTextEquals("cmd"u8);
				reader.Read();

				if (isCommand && reader.TokenType is JsonTokenType.String)
				{
					command = TrimCommand(reader.GetString());
				}
				else
				{
					reader.Skip();
				}
			}
		}

		// Trailing content after the root value
		if (reader.Read())
		{
			throw new JsonException("Unexpected content after the danmu message.");
		}

		return command;
	}

	private static string TrimCommand(string? command)
	{
		command ??= string.Empty;
		int separator = command.IndexOf(':');
		return separator < 0 ? command : command.Substring(0, separator);
	}

	private static DanmuMessage.LiveStopped ParsePreparing(ReadOnlyMemory<byte> json)
	{
		using JsonDocument document = JsonDocument.Parse(json);
		return new DanmuMessage.LiveStopped(document.RootElement.TryGetProperty("round", out _));
	}

	private static DanmuMessage.RoomChanged? ParseRoomChange(ReadOnlyMemory<byte> json)
	{
		using JsonDocument document = JsonDocument.Parse(json);

		if (!document.RootElement.TryGetProperty("data", out JsonElement data) || data.ValueKind is not JsonValueKind.Object)
		{
			return null;
		}

		return new DanmuMessage.RoomChanged(GetString(data, "title") ?? string.Empty, GetString(data, "area_name"), GetString(data, "parent_area_name"));
	}

	private static DanmuMessage.LoginNotice ParseLoginNotice(ReadOnlyMemory<byte> json)
	{
		using JsonDocument document = JsonDocument.Parse(json);
		return new DanmuMessage.LoginNotice(GetString(Get(document.RootElement, "data"), "notice_msg") ?? string.Empty);
	}

	private static DanmuMessage.Comment? ParseComment(ReadOnlyMemory<byte> json, DanmuReceiveTime receivedAt)
	{
		using JsonDocument document = JsonDocument.Parse(json);
		JsonElement info = Get(document.RootElement, "info");

		if (info.ValueKind is not JsonValueKind.Array)
		{
			return null;
		}

		JsonElement meta = At(info, 0);
		JsonElement user = At(info, 2);
		JsonElement extra = At(meta, 15);

		string name = GetString(At(user, 1)) is { Length: > 0 } listedName ? listedName : GetString(Get(Get(Get(extra, "user"), "base"), "name")) ?? string.Empty;
		DanmuSender sender = new(GetInt64(At(user, 0)), name, (GuardLevel)GetInt32(At(info, 7)), ParseMedalArray(At(info, 3)));

		long sentAt = GetInt64(At(meta, 4));
		long sentAtSeconds = GetInt64(Get(At(info, 9), "ts"));
		DateTimeOffset sentTime = sentAt > 0 ? FromUnixMilliseconds(sentAt, receivedAt) : FromUnixSeconds(sentAtSeconds, receivedAt);

		return new DanmuMessage.Comment
		(
			sender,
			GetString(At(info, 1)) ?? string.Empty,
			GetInt32(At(meta, 1)),
			GetInt32(At(meta, 2)),
			GetInt32(At(meta, 3)),
			sentTime,
			GetCommentId(GetString(Get(extra, "extra"))),
			GetString(At(meta, 7)) ?? string.Empty,
			(DanmuCommentKind)GetInt32(At(meta, 12)),
			GetInt64(At(meta, 9)) is not 0
		)
		{ ReceivedAt = receivedAt };
	}

	// The dmid only appears inside the extra string, itself a JSON document
	private static string GetCommentId(string? extra)
	{
		if (string.IsNullOrEmpty(extra))
		{
			return string.Empty;
		}

		try
		{
			using JsonDocument document = JsonDocument.Parse(extra);
			return GetString(Get(document.RootElement, "id_str")) ?? string.Empty;
		}
		catch (JsonException)
		{
			return string.Empty;
		}
	}

	// [level, name, streamer name, room id, color, special, ?, border color, start color, end color, guard level, is lit, streamer uid]
	private static DanmuMedal? ParseMedalArray(JsonElement medal)
	{
		if (medal.ValueKind is not JsonValueKind.Array || GetString(At(medal, 1)) is not { Length: > 0 } name)
		{
			return null;
		}

		return new DanmuMedal(GetInt32(At(medal, 0)), name, GetInt32(At(medal, 4)), GetInt64(At(medal, 12)), GetInt64(At(medal, 3)), GetInt64(At(medal, 11)) is not 0);
	}

	private static DanmuMedal? ParseMedalObject(JsonElement medal)
	{
		if (GetString(medal, "medal_name") is not { Length: > 0 } name)
		{
			return null;
		}

		// medal_color_start matches the color DANMU_MSG lists, medal_color is a different shade written as a string
		return new DanmuMedal(GetInt32(Get(medal, "medal_level")), name, GetInt32(Get(medal, "medal_color_start")), GetInt64(Get(medal, "target_id")), GetInt64(Get(medal, "anchor_roomid")), GetInt64(Get(medal, "is_lighted")) is not 0);
	}

	private static DanmuMessage.SuperChat? ParseSuperChat(ReadOnlyMemory<byte> json, DanmuReceiveTime receivedAt)
	{
		using JsonDocument document = JsonDocument.Parse(json);
		JsonElement data = Get(document.RootElement, "data");

		if (data.ValueKind is not JsonValueKind.Object)
		{
			return null;
		}

		JsonElement userInfo = Get(data, "user_info");
		DanmuSender sender = new(GetInt64(Get(data, "uid")), GetString(userInfo, "uname") ?? string.Empty, (GuardLevel)GetInt32(Get(userInfo, "guard_level")), ParseMedalObject(Get(data, "medal_info")));
		long price = (long)decimal.Round(Math.Clamp(GetDecimal(Get(data, "price")), 0, long.MaxValue / 1000) * 1000);

		return new DanmuMessage.SuperChat(GetInt64(Get(data, "id")), sender, GetString(data, "message") ?? string.Empty, price, TimeSpan.FromSeconds(GetInt64(Get(data, "time"))), FromUnixSeconds(GetInt64(Get(data, "start_time")), receivedAt))
		{
			ReceivedAt = receivedAt
		};
	}

	private static DanmuMessage.Gift? ParseGift(ReadOnlyMemory<byte> json, DanmuReceiveTime receivedAt)
	{
		using JsonDocument document = JsonDocument.Parse(json);
		JsonElement data = Get(document.RootElement, "data");

		if (data.ValueKind is not JsonValueKind.Object)
		{
			return null;
		}

		DanmuSender sender = new(GetInt64(Get(data, "uid")), GetString(data, "uname") ?? string.Empty, (GuardLevel)GetInt32(Get(data, "guard_level")), ParseMedalObject(Get(data, "medal_info")));
		int count = GetInt32(Get(data, "num"));
		long unitPrice = GetInt64(Get(data, "price"));
		long totalPrice = GetInt64(Get(data, "total_coin")) is > 0 and var total ? total : unitPrice * count;

		return new DanmuMessage.Gift(sender, GetInt64(Get(data, "giftId")), GetString(data, "giftName") ?? string.Empty, count, unitPrice, totalPrice, ParseCoinType(GetString(data, "coin_type")), FromUnixSeconds(GetInt64(Get(data, "timestamp")), receivedAt))
		{
			ReceivedAt = receivedAt
		};
	}

	private static void ParseGiftV2(ReadOnlyMemory<byte> json, DanmuReceiveTime receivedAt, List<DanmuMessage> output)
	{
		using JsonDocument document = JsonDocument.Parse(json);

		if (GetString(Get(document.RootElement, "data"), "pb") is not { Length: > 0 } pb)
		{
			return;
		}

		GiftProtobuf.Parse(Convert.FromBase64String(pb), receivedAt, output);
	}

	private static DanmuMessage.SuperChatDeleted? ParseSuperChatDelete(ReadOnlyMemory<byte> json)
	{
		using JsonDocument document = JsonDocument.Parse(json);
		JsonElement ids = Get(Get(document.RootElement, "data"), "ids");

		if (ids.ValueKind is not JsonValueKind.Array)
		{
			return null;
		}

		// 0 is what a missing super chat id reads as, deleting it would hit unrelated super chats
		long[] values = [.. ids.EnumerateArray().Select(GetInt64).Where(static id => id > 0)];
		return values.Length > 0 ? new DanmuMessage.SuperChatDeleted(values) : null;
	}

	private static DanmuMessage.GuardBought? ParseUserToast(ReadOnlyMemory<byte> json, DanmuReceiveTime receivedAt)
	{
		using JsonDocument document = JsonDocument.Parse(json);
		JsonElement data = Get(document.RootElement, "data");

		// Source 2 repeats a purchase already announced with another source, the web page leaves it out of the chat too
		if (data.ValueKind is not JsonValueKind.Object || GetInt64(Get(Get(data, "option"), "source")) is 2)
		{
			return null;
		}

		JsonElement user = Get(data, "sender_uinfo");
		JsonElement guard = Get(data, "guard_info");
		JsonElement pay = Get(data, "pay_info");
		GuardLevel level = (GuardLevel)GetInt32(Get(guard, "guard_level"));
		DanmuSender sender = new(GetInt64(Get(user, "uid")), GetString(Get(user, "base"), "name") ?? string.Empty, level, null);

		// The web page reads neither the price nor the unit, and every captured purchase was a single month; the price is taken per unit like the per-month price of GUARD_BUY.
		// Months are only counted when the unit says so, with another unit the number of months is unknown and the purchase counts as one
		int units = Math.Max(1, GetInt32(Get(pay, "num")));
		int months = GetString(pay, "unit") is null or "" or "月" ? units : 1;
		long unitPrice = Math.Max(0, GetInt64(Get(pay, "price")));
		long price = unitPrice <= long.MaxValue / units ? unitPrice * units : long.MaxValue;
		// 1 opens a guard and 2 renews one, as the toast text of captured messages says; the web page only tells 1 apart from other values, so higher ones count as renewals too, while 0 is what a missing field reads as
		bool isRenewal = GetInt64(Get(guard, "op_type")) >= 2;

		return new DanmuMessage.GuardBought(sender, level, months, price, isRenewal, FromUnixSeconds(GetInt64(Get(guard, "start_time")), receivedAt))
		{
			ReceivedAt = receivedAt
		};
	}

	internal static DanmuCoinType ParseCoinType(string? coinType)
	{
		return coinType is "gold" ? DanmuCoinType.Gold : DanmuCoinType.Silver;
	}

	// A missing or absurd time falls back to the arrival, which is at most seconds later
	internal static DateTimeOffset FromUnixSeconds(long seconds, DanmuReceiveTime receivedAt)
	{
		return seconds > 0 && seconds <= MaxUnixSeconds ? DateTimeOffset.FromUnixTimeSeconds(seconds) : receivedAt.UtcTime;
	}

	private static DateTimeOffset FromUnixMilliseconds(long milliseconds, DanmuReceiveTime receivedAt)
	{
		return milliseconds > 0 && milliseconds <= MaxUnixSeconds * 1000 ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds) : receivedAt.UtcTime;
	}

	private static JsonElement Get(JsonElement element, string name)
	{
		return element.ValueKind is JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) ? value : default;
	}

	private static JsonElement At(JsonElement element, int index)
	{
		return element.ValueKind is JsonValueKind.Array && index < element.GetArrayLength() ? element[index] : default;
	}

	private static string? GetString(JsonElement element, string name)
	{
		return GetString(Get(element, name));
	}

	private static string? GetString(JsonElement element)
	{
		return element.ValueKind is JsonValueKind.String ? element.GetString() : null;
	}

	private static long GetInt64(JsonElement element)
	{
		return element.ValueKind switch
		{
			JsonValueKind.Number when element.TryGetInt64(out long value) => value,
			JsonValueKind.Number when element.TryGetDouble(out double value) => (long)Math.Clamp(value, long.MinValue, long.MaxValue),
			JsonValueKind.String when long.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) => value,
			JsonValueKind.True => 1,
			_ => 0
		};
	}

	private static int GetInt32(JsonElement element)
	{
		return (int)Math.Clamp(GetInt64(element), int.MinValue, int.MaxValue);
	}

	private static decimal GetDecimal(JsonElement element)
	{
		return element.ValueKind switch
		{
			JsonValueKind.Number when element.TryGetDecimal(out decimal value) => value,
			JsonValueKind.String when decimal.TryParse(element.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value) => value,
			_ => 0
		};
	}
}
