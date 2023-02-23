using System.Text.Json.Serialization;

namespace BilibiliApi.Model.PlayUrl;

public class RoomPlayInfo
{
	/// <summary>
	/// 正常返回 0
	/// </summary>
	[JsonPropertyName(@"code")]
	public long Code { get; set; }

	/// <summary>
	/// 正常返回 "0"，否则返回错误信息
	/// </summary>
	[JsonPropertyName(@"message")]
	public string? Message { get; set; }

	/// <summary>
	/// 直播播放地址信息
	/// </summary>
	[JsonPropertyName(@"data")]
	public RoomPlayInfoData? Data { get; set; }
}
