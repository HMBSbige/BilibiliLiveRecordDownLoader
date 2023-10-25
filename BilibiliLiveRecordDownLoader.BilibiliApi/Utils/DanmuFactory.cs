using BilibiliApi.Enums;
using BilibiliApi.Model.Danmu;
using BilibiliApi.Model.Danmu.DanmuBody;
using System.Buffers;
using System.Text;
using System.Text.Json;

namespace BilibiliApi.Utils;

public static class DanmuFactory
{
	public static IDanmu? ParseJson(ReadOnlySequence<byte> body)
	{
		return ParseJson(Encoding.UTF8.GetString(body));
	}

	public static IDanmu? ParseJson(ReadOnlySpan<byte> body)
	{
		string json = Encoding.UTF8.GetString(body);
		return ParseJson(json);
	}

	public static IDanmu? ParseJson(string json)
	{
		using JsonDocument document = JsonDocument.Parse(json);
		JsonElement root = document.RootElement;

		string? cmd = root.GetProperty(@"cmd").GetString();

		switch (cmd)
		{
			case @"LIVE":
			{
				long roomId = root.GetProperty(@"roomid").GetInt64();
				return new StreamStatusDanmu { Cmd = DanmuCommand.LIVE, RoomId = roomId };
			}
			case @"PREPARING":
			{
				long roomId = long.Parse(root.GetProperty(@"roomid").GetString()!);
				if (root.TryGetProperty(@"round", out JsonElement value))
				{
					long round = value.GetInt64();
					return new StreamStatusDanmu { Cmd = DanmuCommand.PREPARING, RoomId = roomId, Round = round };
				}
				return new StreamStatusDanmu
				{
					Cmd = DanmuCommand.PREPARING,
					RoomId = roomId,
					Round = null
				};
			}
			case @"ROOM_CHANGE":
			{
				JsonElement data = root.GetProperty(@"data");
				return new RoomChangeDanmu
				{
					Cmd = DanmuCommand.ROOM_CHANGE,
					Title = data.GetProperty(@"title").GetString(),
					AreaName = data.GetProperty(@"area_name").GetString(),
					ParentAreaName = data.GetProperty(@"parent_area_name").GetString()
				};
			}
		}

		return null;
	}
}
