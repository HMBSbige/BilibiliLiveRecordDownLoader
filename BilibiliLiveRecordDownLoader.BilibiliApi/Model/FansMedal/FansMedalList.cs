using System.Text.Json.Serialization;

namespace BilibiliApi.Model.FansMedal;

public class FansMedalList
{
	/// <summary>
	/// 徽章等级
	/// </summary>
	[JsonPropertyName(@"level")]
	public int medal_level { get; set; }

	/// <summary>
	/// 本日亲密度
	/// </summary>
	[JsonPropertyName(@"today_feed")]
	public long todayFeed { get; set; }

	/// <summary>
	/// 主播名
	/// </summary>
	[JsonPropertyName(@"uname")]
	public string? uname { get; set; }

	/// <summary>
	/// 房间号
	/// </summary>
	[JsonPropertyName(@"roomid")]
	public long roomid { get; set; }
}
