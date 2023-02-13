namespace BilibiliApi.Model.RoomInfo;

public class RoomInfoMessage
{
	/// <summary>
	/// 正常返回 0
	/// </summary>
	public long code { get; set; }

	/// <summary>
	/// 正常返回 "0"，否则返回错误信息
	/// </summary>
	public string? message { get; set; }

	/// <summary>
	/// 房间信息
	/// </summary>
	public RoomInfoData? data { get; set; }

	public class RoomInfoData
	{
		public RoomInfoData_RoomInfo? room_info { get; set; }
		public RoomInfoData_AnchorInfo? anchor_info { get; set; }

		public class RoomInfoData_RoomInfo
		{
			/// <summary>
			/// 真实房间号
			/// </summary>
			public long room_id { get; set; }

			/// <summary>
			/// 短房间号
			/// </summary>
			public long short_id { get; set; }

			/// <summary>
			/// 直播状态，
			/// 2 轮播
			/// 1 开播
			/// 0 未开播
			/// </summary>
			public long live_status { get; set; }

			/// <summary>
			/// 直播间标题
			/// </summary>
			public string? title { get; set; }
		}

		public class RoomInfoData_AnchorInfo
		{
			public RoomInfoData_AnchorInfo_BaseInfo? base_info { get; set; }

			public class RoomInfoData_AnchorInfo_BaseInfo
			{
				public string? uname { get; set; }
			}
		}
	}
}
