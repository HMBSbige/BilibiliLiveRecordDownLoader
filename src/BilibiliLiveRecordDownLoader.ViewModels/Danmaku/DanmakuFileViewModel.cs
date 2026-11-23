using System.Diagnostics.CodeAnalysis;

namespace BilibiliLiveRecordDownLoader.ViewModels.Danmaku;

public sealed partial class DanmakuFileViewModel : ViewModelBase
{
	private readonly IDanmakuAppService _danmaku;
	private DanmakuEntryViewModel[] _all;

	// 不留整份文件，原始条目在行建好后就不再需要
	private DanmakuFileSourceDto _source;
	private bool _isComplete;
	private IReadOnlyList<DanmakuHighlightDto> _highlights;

	public DanmakuFileViewModel(DanmakuFileDto file, IDanmakuAppService danmaku)
	{
		_danmaku = danmaku;
		Path = file.Path;
		FileName = System.IO.Path.GetFileName(file.Path);
		Filter = new DanmakuFilterViewModel().DisposeWith(Disposables);
		Filter.CriteriaChanged.Subscribe(_ => Rebuild()).DisposeWith(Disposables);

		CanRegenerate = this.WhenChanged(static viewModel => viewModel.CanAdjustOffset, static viewModel => viewModel.IsBusy, static (canAdjust, isBusy) => canAdjust && !isBusy);
		CanExport = this.WhenChanged(static viewModel => viewModel.HasVideo);
		JumpToCommand.DisposeWith(Disposables);
		RegenerateCommand.DisposeWith(Disposables);
		ExportHighlightCommand.DisposeWith(Disposables);

		Load(file);
		OnLanguageChanged(UpdateTexts);
	}

	public string Path { get; }

	public string FileName { get; }

	public DanmakuFilterViewModel Filter { get; }

	public RangeObservableCollection<DanmakuEntryViewModel> Entries { get; } = [];

	public IObservable<bool> CanRegenerate { get; }

	public IObservable<bool> CanExport { get; }

	[Reactive]
	public partial string Summary { get; private set; } = string.Empty;

	[Reactive]
	public partial string? Notice { get; private set; }

	[Reactive]
	public partial DanmakuDensityDto Density { get; private set; } = new(TimeSpan.FromSeconds(5), []);

	[Reactive]
	public partial TimeSpan Duration { get; private set; }

	[Reactive]
	public partial IReadOnlyList<DanmakuHighlightViewModel> Highlights { get; private set; } = [];

	[Reactive]
	public partial bool HasHighlights { get; private set; }

	[Reactive]
	public partial bool HasVideo { get; private set; }

	[Reactive]
	public partial bool CanAdjustOffset { get; private set; }

	[Reactive]
	public partial decimal OffsetSeconds { get; set; }

	[Reactive]
	public partial DanmakuEntryViewModel? SelectedEntry { get; set; }

	[Reactive]
	public partial bool HasEntries { get; private set; }

	[Reactive]
	public partial bool HasVisibleEntries { get; private set; }

	[Reactive]
	public partial bool IsBusy { get; private set; }

	[Reactive]
	public partial string? Message { get; private set; }

	[Reactive]
	public partial AppNotificationType MessageType { get; private set; }

	// 应用服务按文件内时间排好了序，可以二分查找
	[ReactiveCommand]
	private void JumpTo(TimeSpan time)
	{
		if (Entries.Count is 0)
		{
			return;
		}

		SelectedEntry = Entries[Math.Min(Entries.LowerBound(entry => entry.Time < time), Entries.Count - 1)];
	}

	[ReactiveCommand(CanExecute = nameof(CanRegenerate))]
	private async Task RegenerateAsync(CancellationToken cancellationToken)
	{
		Message = null;
		IsBusy = true;

		try
		{
			DanmakuOffsetResult result = await _danmaku.ChangeOffsetAsync(Path, ConfigValues.DanmakuOffsetFromSeconds(OffsetSeconds), cancellationToken);
			Load(await _danmaku.OpenFileAsync(Path, cancellationToken));

			(Message, MessageType) = result switch
			{
				DanmakuOffsetResult.Updated { Subtitle: null } => (AppLanguage.Current.DanmakuOffsetSaved, AppNotificationType.Success),
				DanmakuOffsetResult.Updated => (AppLanguage.Current.DanmakuSubtitleRegenerated, AppNotificationType.Success),
				DanmakuOffsetResult.SubtitleNotWritten notWritten => (AppLanguage.Current.DanmakuSubtitleNotWritten.ToString(System.IO.Path.GetFileName(notWritten.Subtitle), notWritten.Error), AppNotificationType.Warning)
			};
		}
		catch (Exception exception) when (IsExpectedFailure(exception))
		{
			ShowError(DescribeFailure(exception, Path));
		}
		finally
		{
			IsBusy = false;
		}
	}

	[ReactiveCommand(CanExecute = nameof(CanExport))]
	private async Task ExportHighlightAsync(DanmakuHighlightViewModel highlight, CancellationToken cancellationToken)
	{
		Message = null;

		try
		{
			string output = await _danmaku.QueueHighlightExportAsync(Path, highlight.Highlight, cancellationToken);
			(Message, MessageType) = (AppLanguage.Current.HighlightExportQueued.ToString(System.IO.Path.GetFileName(output)), AppNotificationType.Success);
		}
		// 找不到的是同名视频，应用服务给的原因已经说清楚了
		catch (Exception exception) when (IsExpectedFailure(exception))
		{
			ShowError(exception.Message);
		}
	}

	// 应用服务对这些异常给出了能直接显示的原因
	internal static bool IsExpectedFailure(Exception exception)
	{
		return exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or ArgumentException;
	}

	// 系统给的找不到文件的原因没有本地化
	internal static string DescribeFailure(Exception exception, string path)
	{
		return exception is FileNotFoundException ? AppLanguage.Current.FileNotFound.ToString(path) : exception.Message;
	}

	private void ShowError(string message)
	{
		(Message, MessageType) = (message, AppNotificationType.Error);
	}

	[MemberNotNull(nameof(_all), nameof(_source), nameof(_highlights))]
	private void Load(DanmakuFileDto file)
	{
		(_source, _isComplete, _highlights) = (file.Source, file.IsComplete, file.Highlights);
		// 先收齐所有删除再标，文件里删除不一定排在 SC 之后
		HashSet<long> deleted = [.. file.Entries.Select(static entry => entry.Entry).OfType<DanmakuEntryDto.SuperChatDeletion>().SelectMany(static deletion => deletion.Ids)];
		_all = [.. file.Entries.Where(static entry => entry.Entry is not DanmakuEntryDto.SuperChatDeletion).Select(DanmakuEntryViewModel.FromFile)];
		DanmakuEntryViewModel.MarkDeleted(_all, deleted);
		Density = file.Density;
		Duration = TimeSpan.FromTicks(Math.Max(file.Entries is [.., var last] ? last.Time.Ticks : 0, file.Density.BinWidth.Ticks * file.Density.Counts.Count));
		HasVideo = file.VideoPath is not null;
		(CanAdjustOffset, OffsetSeconds) = file.Source switch
		{
			DanmakuFileSourceDto.Native native => (true, (decimal)native.Offset.TotalSeconds),
			DanmakuFileSourceDto.Standard => (false, 0m)
		};
		UpdateTexts();
		Rebuild();
	}

	private void Rebuild()
	{
		Entries.Reset(_all.Where(Filter.CreatePredicate()));
		SelectedEntry = null;
		HasEntries = _all.Length > 0;
		HasVisibleEntries = Entries.Count > 0;
	}

	private void UpdateTexts()
	{
		string count = AppLanguage.Current.DanmakuFileSummary.ToString(_all.Count(static entry => entry.Kind is DanmakuEntryKind.Comment).ToString(CultureInfo.CurrentCulture));

		Summary = _source switch
		{
			DanmakuFileSourceDto.Native native => string.Join(" · ", ((string[])[native.Streamer, native.Title, count]).Where(static part => !string.IsNullOrWhiteSpace(part))),
			DanmakuFileSourceDto.Standard => count
		};
		Notice = (_isComplete, _source) switch
		{
			(false, _) => AppLanguage.Current.DanmakuFileIncomplete,
			(true, DanmakuFileSourceDto.Standard) => AppLanguage.Current.DanmakuFileReadOnly,
			(true, DanmakuFileSourceDto.Native) => null
		};
		// 高能列表不虚拟化，行不会重新读取文字，要跟着语言重建
		Highlights = [.. _highlights.Select(static highlight => new DanmakuHighlightViewModel(highlight))];
		HasHighlights = Highlights.Count > 0;
	}
}
