using System;

namespace BilibiliLiveRecordDownLoader.Shared.Utils
{
	public static class Timestamp
	{
		public static long GetTimestamp(DateTime time)
		{
			return (long)time.Subtract(DateTime.UnixEpoch).TotalSeconds;
		}

		public static DateTime GetTime(long timeStamp)
		{
			return DateTime.UnixEpoch.AddSeconds(timeStamp);
		}
	}
}
