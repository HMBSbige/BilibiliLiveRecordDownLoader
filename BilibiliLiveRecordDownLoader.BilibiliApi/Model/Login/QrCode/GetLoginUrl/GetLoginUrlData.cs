namespace BilibiliApi.Model.Login.QrCode.GetLoginUrl;

public class GetLoginUrlData
{
	/// <summary>
	/// 二维码对应 url
	/// </summary>
	public string? url { get; set; }

	/// <summary>
	/// 扫码登录密钥
	/// 应为 32 字符
	/// </summary>
	public string? qrcode_key { get; set; }
}
