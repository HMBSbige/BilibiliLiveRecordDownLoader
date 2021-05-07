using BilibiliApi.Enums;
using BilibiliApi.Model.Danmu;
using BilibiliApi.Model.Danmu.DanmuBody;
using System;
using System.Buffers;
using System.Text;
using System.Text.Json;

namespace BilibiliApi.Utils
{
	public static class DanmuFactory
	{
		public static IDanmu? ParseJson(ReadOnlySequence<byte> body)
		{
			return ParseJson(Encoding.UTF8.GetString(body));
		}

		public static IDanmu? ParseJson(ReadOnlySpan<byte> body)
		{
			var json = Encoding.UTF8.GetString(body);
			return ParseJson(json);
		}

		public static IDanmu? ParseJson(string json)
		{
			using var document = JsonDocument.Parse(json);
			var root = document.RootElement;

			var cmd = root.GetProperty(@"cmd").GetString();

			switch (cmd)
			{
				case @"LIVE":
				{
					var roomId = root.GetProperty(@"roomid").GetInt64();
					return new StreamStatusDanmu { Cmd = DanmuCommand.LIVE, RoomId = roomId };
				}
				case @"PREPARING":
				{
					var roomId = long.Parse(root.GetProperty(@"roomid").GetString()!);
					if (root.TryGetProperty(@"round", out var value))
					{
						var round = value.GetInt64();
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
					var data = root.GetProperty(@"data");
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
}
