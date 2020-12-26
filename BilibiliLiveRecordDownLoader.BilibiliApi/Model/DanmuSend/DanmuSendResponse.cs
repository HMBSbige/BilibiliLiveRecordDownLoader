namespace BilibiliApi.Model.DanmuSend
{
	public class DanmuSendResponse
	{
		/// <summary>
		/// 正常返回 0
		/// </summary>
		public long code { get; set; }

		/// <summary>
		/// 正常返回 空字符串，否则返回错误信息
		/// </summary>
		public string? message { get; set; }
	}
}
