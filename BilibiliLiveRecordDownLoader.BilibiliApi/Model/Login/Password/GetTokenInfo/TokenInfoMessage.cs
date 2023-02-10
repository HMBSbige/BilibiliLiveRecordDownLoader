namespace BilibiliApi.Model.Login.Password.GetTokenInfo;

public class TokenInfoMessage
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
	/// token 信息
	/// </summary>
	public TokenInfoData? data { get; set; }
}
