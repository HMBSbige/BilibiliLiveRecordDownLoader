namespace BilibiliApi.Model.Login.Password.GetTokenInfo;

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
	public long expires_in { get; set; }

	/// <summary>
	/// 是否需要刷新
	/// </summary>
	public bool refresh { get; set; }
}
