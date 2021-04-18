namespace BilibiliApi.Model.LiveRecordDanmu
{
	public class LiveRecordDmInfo
	{
		/// <summary>
		/// 弹幕内容
		/// </summary>
		public string? text { get; set; }

		/// <summary>
		/// 用户昵称
		/// </summary>
		public string? nickname { get; set; }

		/// <summary>
		/// UID
		/// </summary>
		public long uid { get; set; }

		/// <summary>
		/// 时间戳（毫秒），相对于视频开始
		/// </summary>
		public long ts { get; set; }

		public LiveRecordDmCheckInfo? check_info { get; set; }

		/// <summary>
		/// 弹幕类型
		/// </summary>
		public long dm_mode { get; set; }

		/// <summary>
		/// 字体大小
		/// </summary>
		public long dm_fontsize { get; set; }

		/// <summary>
		/// 颜色
		/// </summary>
		public long dm_color { get; set; }

		/// <summary>
		/// uid crc32 
		/// </summary>
		public string? user_hash { get; set; }
	}
}
