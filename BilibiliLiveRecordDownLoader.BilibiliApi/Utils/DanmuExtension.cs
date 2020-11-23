using BilibiliApi.Enums;
using BilibiliApi.Model.Danmu;
using BilibiliApi.Model.Danmu.DanmuBody;

namespace BilibiliApi.Utils
{
	public static class DanmuExtension
	{
		public static LiveStatus IsStreaming(this IDanmu? danmu)
		{
			if (danmu is StreamStatusDanmu streamStatusDanmu)
			{
				if (streamStatusDanmu.Cmd == DanmuCommand.LIVE)
				{
					return LiveStatus.直播;
				}
				return streamStatusDanmu.Round.HasValue ? LiveStatus.轮播 : LiveStatus.闲置;
			}
			return LiveStatus.未知;
		}

		public static string? TitleChanged(this IDanmu? danmu)
		{
			if (danmu is RoomChangeDanmu roomChangeDanmu)
			{
				return roomChangeDanmu.Title ?? string.Empty;
			}
			return null;
		}
	}
}
