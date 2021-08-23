using System;

namespace BilibiliLiveRecordDownLoader.Shared.Utils
{
	public static class Timestamp
	{
		public static long GetTimestamp(DateTime time)
		{
			return new DateTimeOffset(time).ToUnixTimeSeconds();
		}

		public static DateTime GetTime(long timeStamp)
		{
			return DateTimeOffset.FromUnixTimeSeconds(timeStamp).DateTime;
		}
	}
}
