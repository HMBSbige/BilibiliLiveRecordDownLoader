using BilibiliLiveRecordDownLoader.Interfaces;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace BilibiliLiveRecordDownLoader.Models.TaskViewModels;

public abstract class TaskViewModel : ReactiveObject, ITask
{
	#region 属性

	[Reactive]
	public string? Description { get; set; }

	[Reactive]
	public double Progress { get; set; }

	[Reactive]
	public string? Speed { get; set; }

	[Reactive]
	public string Status { get; set; } = @"未开始";

	#endregion

	public abstract Task StartAsync();

	public abstract void Stop();
}
