namespace BilibiliApi.Model.Login.QrCode.GetLoginUrl;

public class GetLoginUrlMessage
{
	/// <summary>
	/// 正常返回 0
	/// </summary>
	public long code { get; set; }

	/// <summary>
	/// 正常返回 true
	/// </summary>
	public bool status { get; set; }

	/// <summary>
	/// 登录地址信息
	/// </summary>
	public GetLoginUrlData? data { get; set; }
}
