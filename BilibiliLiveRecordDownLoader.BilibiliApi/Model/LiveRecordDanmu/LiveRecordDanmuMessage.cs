namespace BilibiliApi.Model.LiveRecordDanmu
{
	public class LiveRecordDanmuMessage
	{
		/// <summary>
		/// 正常返回 0
		/// </summary>
		public long code { get; set; }

		/// <summary>
		/// 正常返回 "0"，否则返回错误信息
		/// </summary>
		public string? message { get; set; }

		/// <summary>
		/// 回放弹幕信息
		/// </summary>
		public LiveRecordDanmuData? data { get; set; }
	}
}
