using BilibiliApi.Enums;
using BilibiliApi.Model.Danmu;
using BilibiliApi.Model.Danmu.DanmuBody;

namespace BilibiliApi.Utils
{
    public static class DanmuExtension
    {
        public static bool? IsStreaming(this IDanmu danmu)
        {
            if (danmu is StreamStatusDanmu streamStatusDanmu)
            {
                return streamStatusDanmu.Cmd == DanmuCommand.LIVE;
            }
            return null;
        }

        public static string TitleChanged(this IDanmu danmu)
        {
            if (danmu is RoomChangeDanmu roomChangeDanmu)
            {
                return roomChangeDanmu.Title;
            }
            return null;
        }
    }
}
