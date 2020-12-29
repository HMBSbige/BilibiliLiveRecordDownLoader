namespace BilibiliApi.Model.Login.Password.OAuth2
{
	public class BilibiliCookie
	{
		public string? name { get; set; }
		public string? value { get; set; }

		/// <summary>
		/// 0/1
		/// </summary>
		public int http_only { get; set; }

		/// <summary>
		/// 有效期，时间戳
		/// </summary>
		public long expires { get; set; }
	}
}