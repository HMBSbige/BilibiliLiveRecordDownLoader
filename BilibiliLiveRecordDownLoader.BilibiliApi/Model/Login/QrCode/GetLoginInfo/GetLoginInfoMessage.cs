using System.Text.Json;

namespace BilibiliApi.Model.Login.QrCode.GetLoginInfo;

public class GetLoginInfoMessage
{
	/// <summary>
	/// 正常为 0
	/// 可能无此字段
	/// </summary>
	public long code { get; set; }

	/// <summary>
	/// 正常为 true
	/// 可能无此字段
	/// </summary>
	public bool status { get; set; }

	/// <summary>
	/// 错误时才有此字段
	/// 不存在该密钥：Not exist oauthKey~
	/// 密钥错误：Can't Match oauthKey~
	/// 未扫描：Can't scan~
	/// 未确认：Can't confirm~
	/// </summary>
	public string? message { get; set; }

	/// <summary>
	/// 不存在该密钥：-1
	/// 密钥错误：-2
	/// 未扫描：-4
	/// 未确认：-5
	/// 正常时："data":{"url":"https://..."}
	/// </summary>
	public JsonElement? data { get; set; }
}
