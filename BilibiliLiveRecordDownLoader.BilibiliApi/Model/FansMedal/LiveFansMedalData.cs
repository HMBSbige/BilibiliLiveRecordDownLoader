namespace BilibiliApi.Model.FansMedal
{
	public class LiveFansMedalData
	{
		/// <summary>
		/// 徽章数
		/// </summary>
		public long count { get; set; }

		/// <summary>
		/// 粉丝徽章列表
		/// </summary>
		public FansMedalList[]? fansMedalList { get; set; }

		/// <summary>
		/// 用户名
		/// </summary>
		public string? name { get; set; }

		/// <summary>
		/// 分页信息
		/// </summary>
		public Pageinfo? pageinfo { get; set; }
	}
}