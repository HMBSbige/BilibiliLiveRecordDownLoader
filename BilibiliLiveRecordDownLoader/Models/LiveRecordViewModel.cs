using BilibiliApi.Model.LiveRecordList;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace BilibiliLiveRecordDownLoader.Models;

public class LiveRecordViewModel : ReactiveObject
{
	#region 属性

	/// <summary>
	/// 视频id
	/// </summary>
	[Reactive]
	public string? Rid { get; set; }

	/// <summary>
	/// 标题
	/// </summary>
	[Reactive]
	public string? Title { get; set; }

	/// <summary>
	/// 封面地址
	/// </summary>
	[Reactive]
	public string? Cover { get; set; }

	/// <summary>
	/// 分区名
	/// </summary>
	[Reactive]
	public string? AreaName { get; set; }

	/// <summary>
	/// 主分区名
	/// </summary>
	[Reactive]
	public string? ParentAreaName { get; set; }

	/// <summary>
	/// 开始时间
	/// </summary>
	[Reactive]
	public DateTime StartTime { get; set; }

	/// <summary>
	/// 结束时间
	/// </summary>
	[Reactive]
	public DateTime EndTime { get; set; }

	/// <summary>
	/// 人气峰值
	/// </summary>
	[Reactive]
	public long Online { get; set; }

	/// <summary>
	/// 弹幕数
	/// </summary>
	[Reactive]
	public long DanmuNum { get; set; }

	/// <summary>
	/// 视频长度
	/// </summary>
	[Reactive]
	public TimeSpan Length { get; set; }

	#endregion

	public LiveRecordViewModel(LiveRecordList data)
	{
		CopyFrom(data);
	}

	public void CopyFrom(LiveRecordList data)
	{
		Rid = data.rid;
		Title = data.title;
		Cover = data.cover;
		AreaName = data.area_name;
		ParentAreaName = data.parent_area_name;
		StartTime = ToDateTime(data.start_timestamp);
		EndTime = ToDateTime(data.end_timestamp);
		Online = data.online;
		DanmuNum = data.danmu_num;
		Length = ToTimeSpan(data.length);
	}

	private static DateTime ToDateTime(long timestamp)
	{
		try
		{
			return DateTime.UnixEpoch.Add(TimeSpan.FromSeconds(timestamp)).ToLocalTime();
		}
		catch
		{
			return default;
		}
	}

	private static TimeSpan ToTimeSpan(long length)
	{
		try
		{
			return TimeSpan.FromMilliseconds(length);
		}
		catch
		{
			return default;
		}
	}
}
