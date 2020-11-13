namespace BilibiliApi.Model.AnchorInfo
{
	public class AnchorInfo
	{
		/// <summary>
		/// 主站 uid
		/// </summary>
		public long uid { get; set; }

		/// <summary>
		/// 昵称
		/// </summary>
		public string? uname { get; set; }

		/// <summary>
		/// 头像地址
		/// </summary>
		public string? face { get; set; }

		/// <summary>
		/// 主站等级
		/// </summary>
		public long platform_user_level { get; set; }
	}
}
