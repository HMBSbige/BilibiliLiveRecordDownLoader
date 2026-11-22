namespace BilibiliLiveRecordDownLoader.ViewModels.Tasks;

public sealed partial class TaskItemViewModel : ReactiveObject
{
	public TaskItemViewModel(BackgroundTaskSnapshot snapshot)
	{
		Id = snapshot.Id;
		Title = snapshot.Title;
		TargetPath = snapshot.TargetPath;
		// 标题已包含源文件名
		Paths = snapshot.TargetPath is { } target ? $"→ {Path.GetFileName(target)}" : null;
		PathsTip = string.Join(Environment.NewLine, new[] { snapshot.SourcePath, snapshot.TargetPath }.OfType<string>());
		Apply(snapshot);
	}

	public Guid Id { get; }

	public string Title { get; }

	public string? TargetPath { get; }

	public string? Paths { get; }

	public string PathsTip { get; }

	[Reactive]
	public partial BackgroundTaskStatus Status { get; private set; }

	[Reactive]
	public partial double Progress { get; private set; }

	[Reactive]
	public partial bool IsIndeterminate { get; private set; }

	[Reactive]
	public partial bool IsActive { get; private set; }

	[Reactive]
	public partial string? Details { get; private set; }

	[Reactive]
	public partial string? Message { get; private set; }

	internal void Apply(BackgroundTaskSnapshot snapshot)
	{
		Status = snapshot.Status;
		Progress = (snapshot.Progress ?? 0) * 100;
		IsActive = snapshot.Status is BackgroundTaskStatus.Queued or BackgroundTaskStatus.Running;
		IsIndeterminate = snapshot is { Status: BackgroundTaskStatus.Running, Progress: null };
		Details = snapshot.Status is BackgroundTaskStatus.Running ? FormatDetails(snapshot.Progress, snapshot.Speed) : null;
		Message = snapshot.Status is BackgroundTaskStatus.Failed ? snapshot.Message : null;
	}

	private static string? FormatDetails(double? progress, double? speed)
	{
		string?[] parts =
		[
			progress is { } value ? value.ToString("P0", CultureInfo.CurrentCulture) : null,
			speed is { } factor ? factor.ToString("0.0", CultureInfo.CurrentCulture) + "×" : null
		];

		return parts is [null, null] ? null : string.Join(" · ", parts.OfType<string>());
	}
}
