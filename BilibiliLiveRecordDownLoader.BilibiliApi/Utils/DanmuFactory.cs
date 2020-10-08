using BilibiliApi.Enums;
using BilibiliApi.Model.Danmu;
using BilibiliApi.Model.Danmu.DanmuBody;
using System;
using System.Text;
using System.Text.Json;

namespace BilibiliApi.Utils
{
    public static class DanmuFactory
    {
        public static IDanmu GetDanmu(DanmuPacket packet)
        {
            switch (packet.Operation)
            {
                case Operation.SendMsgReply:
                {
                    var danmu = ParseJson(packet.Body.Span);
                    if (danmu is null)
                    {
                        break;
                    }
                    return danmu;
                }
            }

            return CreateDefaultDanmu();
        }

        public static IDanmu CreateDefaultDanmu()
        {
            return new DanmuBase { Cmd = DanmuCommand.Unknown };
        }

        public static IDanmu ParseJson(Span<byte> body)
        {
            var json = Encoding.UTF8.GetString(body);
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
                    var roomId = long.Parse(root.GetProperty(@"roomid").GetString());
                    return new StreamStatusDanmu { Cmd = DanmuCommand.PREPARING, RoomId = roomId };
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
