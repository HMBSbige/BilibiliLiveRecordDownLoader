using System.Globalization;
using System.Text.Json;
using System.Text.Unicode;

namespace BilibiliLive.Danmu;

// 服务器版本之间会缺字段或改字段类型，所以每次读取都退回默认值而不是抛异常
internal static class DanmuMessageParser
{
	private static readonly long MaxUnixSeconds = DateTimeOffset.MaxValue.ToUnixTimeSeconds();

	// 远超最长的档位，也让离谱的值不超出 TimeSpan 的范围
	private const long MaxSuperChatSeconds = 24 * 60 * 60;

	// 换算成千分之一元后仍能放进 long 的最大整元价格
	private const long MaxSuperChatPrice = long.MaxValue / 1000;

	/// <summary>抛出异常时，<paramref name="output"/> 里最多只多了原始消息</summary>
	/// <exception cref="JsonException">包体不是合法的 JSON</exception>
	/// <exception cref="InvalidDataException">礼物的 protobuf 格式错误</exception>
	/// <exception cref="FormatException">礼物的 base64 格式错误</exception>
	/// <exception cref="InvalidOperationException">字符串里有非法的 UTF-8 或孤立代理项的转义</exception>
	public static void Parse(ReadOnlyMemory<byte> json, DanmuParseMode mode, DanmuReceiveTime receivedAt, List<DanmuMessage> output)
	{
		string? command;

		if (mode.HasFlag(DanmuParseMode.Raw))
		{
			// 原始消息会原样写进 JSON Lines 文件，所以必须整体合法，不能只合法到 cmd 为止
			command = ValidateAndGetCommand(json.Span);

			if (command is not null)
			{
				output.Add(new DanmuMessage.Raw(command, json.ToArray()) { ReceivedAt = receivedAt });
			}
		}
		else
		{
			// 大部分流量是没人要的聊天消息，只读 cmd 就能丢掉
			command = GetCommand(json.Span);
		}

		int count = output.Count;

		try
		{
			ParseCommand(command, json, mode.HasFlag(DanmuParseMode.Chat), receivedAt, output);
		}
		catch
		{
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
			// 只在认证后到一次，所以不管解不解析聊天都产出，后来开始看聊天的订阅者也能知道昵称是否打码
			case "LOG_IN_NOTICE":
			{
				output.Add(new DanmuMessage.LoginNotice { ReceivedAt = receivedAt });
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
			// 每次购买也会以 GUARD_BUY 到达，但那里只有标价；网页显示的购买来自这一条
			case "USER_TOAST_MSG_V2" when includeChat && ParseUserToast(json, receivedAt) is { } guard:
			{
				output.Add(guard);
				break;
			}
		}
	}

	public static long? GetAuthReplyCode(ReadOnlyMemory<byte> json)
	{
		try
		{
			using JsonDocument document = JsonDocument.Parse(json);
			return Get(document.RootElement, "code") is { ValueKind: JsonValueKind.Number } code && code.TryGetInt64(out long value) ? value : null;
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private static string? GetCommand(ReadOnlySpan<byte> json)
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

	private static string? ValidateAndGetCommand(ReadOnlySpan<byte> json)
	{
		// 读取器要到 GetString 才检查属性名和字符串里的 UTF-8，而原文会被原样写进 JSON Lines 文件
		if (!Utf8.IsValid(json))
		{
			throw new JsonException("Danmu message is not valid UTF-8.");
		}

		// 读取器本身就会拒绝空输入、格式错误的 JSON 和多余的尾随内容
		Utf8JsonReader reader = new(json);

		while (reader.Read())
		{
		}

		return GetCommand(json);
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
		return new DanmuMessage.LiveStopped(GetInt64(Get(document.RootElement, "round")) is not 0);
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
			GetString(At(meta, 7)) ?? string.Empty,
			GetInt64(At(meta, 9)) is not 0
		)
		{ ReceivedAt = receivedAt };
	}

	// [等级, 名称, 主播名, 房间号, 颜色, 特殊, ?, 边框颜色, 渐变起始色, 渐变结束色, 大航海等级, 是否点亮, 主播 uid]
	private static DanmuMedal? ParseMedalArray(JsonElement medal)
	{
		if (medal.ValueKind is not JsonValueKind.Array || GetString(At(medal, 1)) is not { Length: > 0 } name)
		{
			return null;
		}

		return new DanmuMedal(GetInt32(At(medal, 0)), name, GetInt32(At(medal, 4)), GetInt64(At(medal, 11)) is not 0);
	}

	private static DanmuMedal? ParseMedalObject(JsonElement medal)
	{
		if (GetString(medal, "medal_name") is not { Length: > 0 } name)
		{
			return null;
		}

		// medal_color_start 和 DANMU_MSG 列出的颜色一致，medal_color 是另一种色调，而且写成字符串
		return new DanmuMedal(GetInt32(Get(medal, "medal_level")), name, GetInt32(Get(medal, "medal_color_start")), GetInt64(Get(medal, "is_lighted")) is not 0);
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
		long price = (long)decimal.Round(Math.Clamp(GetDecimal(Get(data, "price")), 0, MaxSuperChatPrice) * 1000);
		TimeSpan duration = GetInt64(Get(data, "time")) is > 0 and <= MaxSuperChatSeconds and var seconds ? TimeSpan.FromSeconds(seconds) : TimeSpan.Zero;

		return new DanmuMessage.SuperChat(GetInt64(Get(data, "id")), sender, GetString(data, "message") ?? string.Empty, price, duration)
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
		long totalPrice = GiftTotalPrice(GetInt64(Get(data, "total_coin")), GetInt64(Get(data, "price")), count);

		return new DanmuMessage.Gift(sender, GetString(data, "giftName") ?? string.Empty, count, totalPrice, ParseCoinType(GetString(data, "coin_type")))
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

		// 缺失的醒目留言 id 读出来是 0，按它删除会误删无关的醒目留言
		long[] values = [.. ids.EnumerateArray().Select(GetInt64).Where(static id => id > 0)];
		return values.Length > 0 ? new DanmuMessage.SuperChatDeleted(values) : null;
	}

	private static DanmuMessage.GuardBought? ParseUserToast(ReadOnlyMemory<byte> json, DanmuReceiveTime receivedAt)
	{
		using JsonDocument document = JsonDocument.Parse(json);
		JsonElement data = Get(document.RootElement, "data");

		// source 为 2 的是重复播报已经以其他 source 通知过的购买，网页也不把它放进聊天
		if (data.ValueKind is not JsonValueKind.Object || GetInt64(Get(Get(data, "option"), "source")) is 2)
		{
			return null;
		}

		JsonElement user = Get(data, "sender_uinfo");
		JsonElement guard = Get(data, "guard_info");
		JsonElement pay = Get(data, "pay_info");
		GuardLevel level = (GuardLevel)GetInt32(Get(guard, "guard_level"));
		DanmuSender sender = new(GetInt64(Get(user, "uid")), GetString(Get(user, "base"), "name") ?? string.Empty, level, null);

		// 抓到的购买都是单月，网页既不读价格也不读单位，所以价格按单位计，和 GUARD_BUY 的每月价格一样
		int units = Math.Max(1, GetInt32(Get(pay, "num")));
		int months = GetString(pay, "unit") is null or "" or "月" ? units : 1;
		long price = TotalPrice(GetInt64(Get(pay, "price")), units);
		// 抓到的通知里 1 是开通、2 是续费；网页把 1 以外的都当续费，但 0 是缺了这个字段
		bool isRenewal = GetInt64(Get(guard, "op_type")) >= 2;

		return new DanmuMessage.GuardBought(sender, level, months, price, isRenewal)
		{
			ReceivedAt = receivedAt
		};
	}

	internal static DanmuCoinType ParseCoinType(string? coinType)
	{
		return coinType is "gold" ? DanmuCoinType.Gold : DanmuCoinType.Silver;
	}

	internal static long GiftTotalPrice(long totalPrice, long unitPrice, int count)
	{
		return totalPrice > 0 ? totalPrice : TotalPrice(unitPrice, count);
	}

	// 离谱的单价或数量相乘会溢出成负数，再经 PaidPrice 进入营收统计
	private static long TotalPrice(long unitPrice, int count)
	{
		return unitPrice > 0 && count > 0 ? long.CreateSaturating(Math.BigMul(unitPrice, count)) : 0;
	}

	// 时间缺失或离谱时退回到达时间，两者最多差几秒
	private static DateTimeOffset FromUnixSeconds(long seconds, DanmuReceiveTime receivedAt)
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
