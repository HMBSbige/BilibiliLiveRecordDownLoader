namespace BilibiliLiveRecordDownLoader.ViewModels.Danmaku;

// 录好的弹幕文件；列表按文件内时间排序
public sealed partial class DanmakuFileViewModel : ViewModelBase
{
	private readonly IDanmakuAppService _danmaku;
	private DanmakuFileDto _file;
	private DanmakuEntryViewModel[] _all = [];

	public DanmakuFileViewModel(DanmakuFileDto file, IDanmakuAppService danmaku)
	{
		_danmaku = danmaku;
		_file = file;
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

	public DanmakuEntryCollection Entries { get; } = [];

	public IObservable<bool> CanRegenerate { get; }

	public IObservable<bool> CanExport { get; }

	// 主播、标题和弹幕条数
	[Reactive]
	public partial string Summary { get; private set; } = string.Empty;

	// 文件不完整或者不是本程序写的时说明一下；没什么要说的时为 null
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

	// 有同名视频才能导出高能片段
	[Reactive]
	public partial bool HasVideo { get; private set; }

	// 只有本程序写的文件能改偏移
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

	// 最近一次操作的结果；为 null 时不显示
	[Reactive]
	public partial string? Message { get; private set; }

	[Reactive]
	public partial AppNotificationType MessageType { get; private set; }

	// 选中不早于这个时间的第一条，被过滤掉时取下一条可见的，都在它之前时取最后一条
	[ReactiveCommand]
	private void JumpTo(TimeSpan time)
	{
		if (Entries.Count is 0)
		{
			return;
		}

		int low = 0;
		int high = Entries.Count;

		while (low < high)
		{
			int middle = low + (high - low) / 2;

			if (Entries[middle].Time < time)
			{
				low = middle + 1;
			}
			else
			{
				high = middle;
			}
		}

		SelectedEntry = Entries[Math.Min(low, Entries.Count - 1)];
	}

	// 改写后重新读取，列表里的时间跟着新的偏移变
	[ReactiveCommand(CanExecute = nameof(CanRegenerate))]
	private async Task RegenerateAsync(CancellationToken cancellationToken)
	{
		Message = null;
		IsBusy = true;

		try
		{
			DanmakuOffsetResult result = await _danmaku.ChangeOffsetAsync(Path, TimeSpan.FromSeconds((double)OffsetSeconds), cancellationToken);
			Load(await _danmaku.OpenFileAsync(Path, cancellationToken));

			(Message, MessageType) = result switch
			{
				DanmakuOffsetResult.Updated { Subtitle: null } => (AppLanguage.Current.DanmakuOffsetSaved, AppNotificationType.Success),
				DanmakuOffsetResult.Updated => (AppLanguage.Current.DanmakuSubtitleRegenerated, AppNotificationType.Success),
				DanmakuOffsetResult.SubtitleInUse inUse => (AppLanguage.Current.DanmakuSubtitleInUse.ToString(System.IO.Path.GetFileName(inUse.Subtitle)), AppNotificationType.Warning)
			};
		}
		catch (Exception exception) when (IsExpectedFailure(exception))
		{
			ShowError(exception);
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
		catch (Exception exception) when (IsExpectedFailure(exception))
		{
			ShowError(exception);
		}
	}

	// 应用服务对这些情况都给出了能直接显示的原因，其余的交给全局处理
	internal static bool IsExpectedFailure(Exception exception)
	{
		return exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or ArgumentException;
	}

	private void ShowError(Exception exception)
	{
		string message = exception is FileNotFoundException { FileName: { } fileName } && fileName == Path ? AppLanguage.Current.FileNotFound.ToString(Path) : exception.Message;
		(Message, MessageType) = (message, AppNotificationType.Error);
	}

	private void Load(DanmakuFileDto file)
	{
		_file = file;
		// 删除不单独列出，只标出被删的 SC；先收齐所有删除再标，不要求文件里删除排在 SC 之后
		HashSet<long> deleted = [.. file.Entries.Select(static entry => entry.Entry).OfType<DanmakuEntryDto.SuperChatDeletion>().SelectMany(static deletion => deletion.Ids)];
		_all = [.. file.Entries.Select(static (entry, index) => (Entry: entry, Index: index)).Where(static item => item.Entry.Entry is not DanmakuEntryDto.SuperChatDeletion).Select(static item => DanmakuEntryViewModel.FromFile(item.Index, item.Entry))];
		DanmakuEntryViewModel.MarkDeleted(_all, deleted);
		Density = file.Density;
		Duration = TimeSpan.FromTicks(Math.Max(file.Entries is [.., var last] ? last.Time.Ticks : 0, file.Density.BinWidth.Ticks * file.Density.Counts.Count));
		Highlights = [.. file.Highlights.Select(static highlight => new DanmakuHighlightViewModel(highlight))];
		HasHighlights = Highlights.Count > 0;
		HasVideo = file.VideoPath is not null;
		(CanAdjustOffset, OffsetSeconds) = file.Source switch
		{
			DanmakuFileSourceDto.Native native => (true, (decimal)native.Header.Offset.TotalSeconds),
			DanmakuFileSourceDto.Standard => (false, 0m)
		};
		UpdateTexts();
		Rebuild();
	}

	private void Rebuild()
	{
		Entries.Reset(_all.Where(Filter.Matches));
		SelectedEntry = null;
		HasEntries = _all.Length > 0;
		HasVisibleEntries = Entries.Count > 0;
	}

	private void UpdateTexts()
	{
		string count = AppLanguage.Current.DanmakuFileSummary.ToString(_all.Count(static entry => entry.Kind is DanmakuEntryKind.Comment).ToString(CultureInfo.CurrentCulture));

		Summary = _file.Source switch
		{
			DanmakuFileSourceDto.Native { Header: var header } => string.Join(" · ", ((string[])[header.Streamer, header.Title, count]).Where(static part => !string.IsNullOrWhiteSpace(part))),
			DanmakuFileSourceDto.Standard => count
		};
		Notice = (_file.IsComplete, _file.Source) switch
		{
			(false, _) => AppLanguage.Current.DanmakuFileIncomplete,
			(_, DanmakuFileSourceDto.Standard) => AppLanguage.Current.DanmakuFileReadOnly,
			_ => null
		};
	}
}
