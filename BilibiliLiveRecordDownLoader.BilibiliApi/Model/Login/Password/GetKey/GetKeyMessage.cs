namespace BilibiliApi.Model.Login.Password.GetKey;

public class GetKeyMessage
{
	/// <summary>
	/// 正常为 0
	/// </summary>
	public long code { get; set; }

	public GetKeyData? data { get; set; }
}
