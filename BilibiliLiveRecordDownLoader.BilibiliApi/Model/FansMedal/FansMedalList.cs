namespace BilibiliApi.Model.FansMedal
{
	public class FansMedalList
	{
		/// <summary>
		/// 徽章等级
		/// </summary>
		public int medal_level { get; set; }

		/// <summary>
		/// 本日亲密度
		/// </summary>
		public long todayFeed { get; set; }

		/// <summary>
		/// 主播名
		/// </summary>
		public string? uname { get; set; }

		/// <summary>
		/// 房间号
		/// </summary>
		public long roomid { get; set; }
	}
}
