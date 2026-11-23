namespace BilibiliLiveRecordDownLoader.ViewModels.Danmaku;

public sealed partial class DanmakuFilterViewModel : ViewModelBase
{
	private static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(200);

	public DanmakuFilterViewModel()
	{
		CriteriaChanged = Signal.Merge
		(
			this.WhenChanged(static viewModel => viewModel.ShowComments, static viewModel => viewModel.ShowSuperChats, static viewModel => viewModel.ShowGifts, static viewModel => viewModel.ShowGuards, static (_, _, _, _) => RxVoid.Default).Skip(1),
			this.WhenChanged(static viewModel => viewModel.SearchText)
				.Skip(1)
				.Calm(SearchDelay, RxSchedulers.TaskpoolScheduler)
				.ObserveOn(RxSchedulers.MainThreadScheduler)
				.Map(static _ => RxVoid.Default)
		);
	}

	// 在界面线程上触发
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

	public Func<DanmakuEntryViewModel, bool> CreatePredicate()
	{
		(bool comments, bool superChats, bool gifts, bool guards) = (ShowComments, ShowSuperChats, ShowGifts, ShowGuards);
		string? search = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim();

		return entry => entry.Kind switch
		{
			DanmakuEntryKind.Comment => comments,
			DanmakuEntryKind.SuperChat => superChats,
			DanmakuEntryKind.Gift => gifts,
			DanmakuEntryKind.Guard => guards,
			DanmakuEntryKind.Notice => true
		} && (search is null || entry.Contains(search));
	}
}
