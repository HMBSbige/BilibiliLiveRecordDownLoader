namespace BilibiliApi.Model.LiveRecordDanmu
{
	public class LiveRecordDmCheckInfo
	{
		/// <summary>
		/// 弹幕发送时的时间戳，毫秒
		/// </summary>
		public long ts { get; set; }
		public string? check_token { get; set; }
	}
}
