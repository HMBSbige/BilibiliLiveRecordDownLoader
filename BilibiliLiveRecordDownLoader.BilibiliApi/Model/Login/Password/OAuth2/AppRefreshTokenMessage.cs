namespace BilibiliApi.Model.Login.Password.OAuth2
{
	public class AppRefreshTokenMessage
	{
		/// <summary>
		/// 当前时间戳
		/// </summary>
		public long ts { get; set; }

		/// <summary>
		/// 正常返回 0
		/// </summary>
		public long code { get; set; }
		public TokenInfo? data { get; set; }
	}
}
