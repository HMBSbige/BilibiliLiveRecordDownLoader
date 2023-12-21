namespace BilibiliApi.Model.Login.QrCode.GetLoginInfo;

public class GetLoginInfoMessage
{
	public GetLoginInfoMessageData? data { get; set; }
}

public record GetLoginInfoMessageData
{
	public string? url { get; set; }

	public string? refresh_token { get; set; }

	/// <summary>
	/// 登录时间
	/// </summary>
	public long timestamp { get; set; }

	/// <summary>
	/// 0：扫码登录成功
	/// 86038：二维码已失效
	/// 86090：二维码已扫码未确认
	/// 86101：未扫码
	/// </summary>
	public long code { get; set; }

	/// <summary>
	/// 扫码状态信息
	/// </summary>
	public string? message { get; set; }
}
