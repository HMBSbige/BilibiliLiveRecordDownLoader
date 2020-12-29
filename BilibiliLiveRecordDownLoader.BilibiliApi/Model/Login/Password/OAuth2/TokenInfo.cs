namespace BilibiliApi.Model.Login.Password.OAuth2
{
	public class TokenInfo
	{
		/// <summary>
		/// uid
		/// </summary>
		public long mid { get; set; }

		public string? access_token { get; set; }
		public string? refresh_token { get; set; }

		/// <summary>
		/// 剩余有效时间，单位秒
		/// </summary>
		public long expires_in { get; set; }
	}
}