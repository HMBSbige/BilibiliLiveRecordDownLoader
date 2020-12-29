using System;

namespace BilibiliLiveRecordDownLoader.Shared.Utils
{
	public static class Timestamp
	{
		public static string GetTimestamp(DateTime time)
		{
			return ((ulong)time.Subtract(DateTime.UnixEpoch).TotalSeconds).ToString();
		}

		public static DateTime GetTime(string timeStamp)
		{
			var time = ulong.Parse(timeStamp);
			return DateTime.UnixEpoch.AddSeconds(time);
		}
	}
}
