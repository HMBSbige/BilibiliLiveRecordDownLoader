namespace BilibiliApi.Model.Login.Password.OAuth2
{
	public class AppLoginMessage
	{
		/// <summary>
		/// 当前时间戳
		/// </summary>
		public long ts { get; set; }

		/// <summary>
		/// 正常为 0
		/// </summary>
		public long code { get; set; }

		/// <summary>
		/// 登录信息
		/// </summary>
		public AppLoginData? data { get; set; }
	}
}
