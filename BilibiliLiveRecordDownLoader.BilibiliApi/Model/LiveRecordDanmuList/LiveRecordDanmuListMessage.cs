namespace BilibiliApi.Model.LiveRecordDanmuList
{
	public class LiveRecordDanmuListMessage
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
		/// 回放弹幕列表信息
		/// </summary>
		public LiveRecordDanmuListData? data { get; set; }
	}
}
