namespace BilibiliApi.Model.DanmuConf
{
	public class DanmuConfMessage
	{
		/// <summary>
		/// 正常返回 0
		/// </summary>
		public long code { get; set; }

		/// <summary>
		/// 正常返回 "ok"，否则返回错误信息
		/// </summary>
		public string? msg { get; set; }

		/// <summary>
		/// 正常返回 "ok"，否则返回错误信息
		/// </summary>
		public string? message { get; set; }

		/// <summary>
		/// 弹幕服务器信息
		/// </summary>
		public DanmuConfData? data { get; set; }
	}
}
