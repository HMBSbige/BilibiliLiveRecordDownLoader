namespace BilibiliApi.Model.LiveRecordDanmu
{
	public class LiveRecordDmInfo
	{
		public string? text { get; set; }
		public string? nickname { get; set; }
		public long uid { get; set; }
		public string? uname_color { get; set; }
		public long ts { get; set; }
		public long is_admin { get; set; }
		public long vip { get; set; }
		public long svip { get; set; }
		public string[]? medal { get; set; }
		public string[]? title { get; set; }
		public string[]? user_level { get; set; }
		public long rank { get; set; }
		public long mobile_verify { get; set; }
		public long guard_level { get; set; }
		public long bubble { get; set; }
		public LiveRecordDmCheckInfo? check_info { get; set; }
		public long dm_type { get; set; }
		public long msg_type { get; set; }
		public string? dm_id { get; set; }
		public long dm_mode { get; set; }
		public long dm_fontsize { get; set; }
		public long dm_color { get; set; }
		public string? user_hash { get; set; }
	}
}
