namespace BilibiliApi.Model.Login.Password.GetTokenInfo
{
	public class TokenInfoData
	{
		/// <summary>
		/// uid
		/// </summary>
		public long mid { get; set; }

		public string? access_token { get; set; }

		/// <summary>
		/// 剩余有效时间，单位秒
		/// </summary>
		public int expires_in { get; set; }

		/// <summary>
		/// 用户名
		/// </summary>
		public string? userid { get; set; }

		/// <summary>
		/// 用户名
		/// </summary>
		public string? uname { get; set; }
	}
}
