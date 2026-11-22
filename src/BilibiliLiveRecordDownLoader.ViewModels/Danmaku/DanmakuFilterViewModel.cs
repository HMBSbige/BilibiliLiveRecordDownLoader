namespace BilibiliLiveRecordDownLoader.ViewModels.Danmaku;

public sealed partial class DanmakuFilterViewModel : ViewModelBase
{
	private static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(200);

	public DanmakuFilterViewModel()
	{
		// 输入框停顿后再过滤，类别开关立即生效
		CriteriaChanged = Signal.Merge
		(
			this.WhenChanged(static viewModel => viewModel.ShowComments).Skip(1).Map(static _ => RxVoid.Default),
			this.WhenChanged(static viewModel => viewModel.ShowSuperChats).Skip(1).Map(static _ => RxVoid.Default),
			this.WhenChanged(static viewModel => viewModel.ShowGifts).Skip(1).Map(static _ => RxVoid.Default),
			this.WhenChanged(static viewModel => viewModel.ShowGuards).Skip(1).Map(static _ => RxVoid.Default),
			this.WhenChanged(static viewModel => viewModel.SearchText)
				.Skip(1)
				.Calm(SearchDelay, RxSchedulers.TaskpoolScheduler)
				.ObserveOn(RxSchedulers.MainThreadScheduler)
				.Map(static _ => RxVoid.Default)
		);
	}

	// 条件变化时触发，在界面线程上
	public IObservable<RxVoid> CriteriaChanged { get; }

	[Reactive]
	public partial bool ShowComments { get; set; } = true;

	[Reactive]
	public partial bool ShowSuperChats { get; set; } = true;

	[Reactive]
	public partial bool ShowGifts { get; set; } = true;

	[Reactive]
	public partial bool ShowGuards { get; set; } = true;

	[Reactive]
	public partial string? SearchText { get; set; }

	// 标题变化、断开这类提示条数很少，不受类别开关影响
	public bool Matches(DanmakuEntryViewModel entry)
	{
		bool isShown = entry.Kind switch
		{
			DanmakuEntryKind.Comment => ShowComments,
			DanmakuEntryKind.SuperChat => ShowSuperChats,
			DanmakuEntryKind.Gift => ShowGifts,
			DanmakuEntryKind.Guard => ShowGuards,
			_ => true
		};

		return isShown && (string.IsNullOrWhiteSpace(SearchText) || entry.Contains(SearchText.Trim()));
	}
}
