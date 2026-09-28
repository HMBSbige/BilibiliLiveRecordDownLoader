using System.Text.Json;

namespace BilibiliLive.Danmu;

internal static class DanmuMessageParser
{
	public static DanmuMessage? Parse(ReadOnlyMemory<byte> json)
	{
		// Only a few commands matter, so the bulk of traffic (DANMU_MSG and friends) is rejected without building a document
		return GetCommand(json.Span) switch
		{
			"LIVE" => new DanmuMessage.LiveStarted(),
			"PREPARING" => ParsePreparing(json),
			"ROOM_CHANGE" => ParseRoomChange(json),
			_ => null
		};
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
				if (reader.TokenType is not JsonTokenType.String)
				{
					return null;
				}

				string command = reader.GetString() ?? string.Empty;
				int separator = command.IndexOf(':');
				return separator < 0 ? command : command.Substring(0, separator);
			}

			reader.Skip();
		}

		return null;
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

		static string? GetString(JsonElement element, string name)
		{
			return element.TryGetProperty(name, out JsonElement value) && value.ValueKind is JsonValueKind.String ? value.GetString() : null;
		}
	}
}
