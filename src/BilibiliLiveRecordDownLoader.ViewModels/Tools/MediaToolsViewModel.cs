namespace BilibiliLiveRecordDownLoader.ViewModels.Tools;

public sealed partial class MediaToolsViewModel : ViewModelBase
{
	private static readonly string[] RemuxableExtensions = [".flv", ".ts", ".mkv", ".m4s"];

	private readonly HashSet<string> _queuedCutOutputs = new(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
	private string? _ffmpegVersion;

	public MediaToolsViewModel()
	{
		CanQueueRemux = this.WhenChanged(static viewModel => viewModel.IsFFmpegAvailable, static viewModel => viewModel.RemuxInput, static viewModel => viewModel.RemuxOutput, CanQueue);
		CanQueueCut = this.WhenChanged(static viewModel => viewModel.IsFFmpegAvailable, static viewModel => viewModel.CutInput, static viewModel => viewModel.CutOutput, CanQueue);

		CheckFFmpegCommand.DisposeWith(Disposables);
		DownloadFFmpegCommand.DisposeWith(Disposables);
		BrowseRemuxInputCommand.DisposeWith(Disposables);
		BrowseRemuxOutputCommand.DisposeWith(Disposables);
		QueueRemuxCommand.DisposeWith(Disposables);
		BrowseCutInputCommand.DisposeWith(Disposables);
		BrowseCutOutputCommand.DisposeWith(Disposables);
		QueueCutCommand.DisposeWith(Disposables);

		this.WhenChanged(static viewModel => viewModel.RemuxInput)
			.Skip(1)
			.Subscribe(input => RemuxOutput = SuggestRemuxOutput(input))
			.DisposeWith(Disposables);

		this.WhenChanged(static viewModel => viewModel.CutInput)
			.Skip(1)
			.Subscribe(input => CutOutput = SuggestCutOutput(input, _queuedCutOutputs))
			.DisposeWith(Disposables);

		OnLanguageChanged(UpdateFFmpegVersionText);
	}

	public IObservable<bool> CanQueueRemux { get; }

	public IObservable<bool> CanQueueCut { get; }

	[Reactive]
	public partial bool? IsFFmpegAvailable { get; private set; }

	[Reactive]
	public partial string? FFmpegVersion { get; private set; }

	[Reactive]
	public partial string RemuxInput { get; set; } = string.Empty;

	[Reactive]
	public partial string RemuxOutput { get; set; } = string.Empty;

	[Reactive]
	public partial bool DeleteRemuxSource { get; set; }

	[Reactive]
	public partial bool RepairFlv { get; set; }

	[Reactive]
	public partial string CutInput { get; set; } = string.Empty;

	[Reactive]
	public partial string CutOutput { get; set; } = string.Empty;

	[Reactive]
	public partial string CutStart { get; set; } = "00:00:00.000";

	[Reactive]
	public partial string CutEnd { get; set; } = "01:00:00.000";

	internal void RefreshFFmpegStatus()
	{
		Forget(CheckFFmpegAsync);
	}

	[ReactiveCommand]
	private async Task CheckFFmpegAsync(CancellationToken cancellationToken)
	{
		IsFFmpegAvailable = null;
		_ffmpegVersion = await AppLocator.Current.GetRequiredService<IMediaToolAppService>().GetFFmpegVersionAsync(cancellationToken);
		UpdateFFmpegVersionText();
		IsFFmpegAvailable = _ffmpegVersion is not null;
	}

	[ReactiveCommand]
	private async Task DownloadFFmpegAsync()
	{
		await AppLocator.Current.GetRequiredService<ILauncherService>().LaunchUriAsync(new Uri(AppConsts.FFmpegDownloadUrl));
	}

	[ReactiveCommand]
	private async Task BrowseRemuxInputAsync()
	{
		if (await AppLocator.Current.GetRequiredService<IStoragePickerService>().PickVideoFileAsync(GetDirectory(RemuxInput)) is { } path)
		{
			RemuxInput = path;
		}
	}

	[ReactiveCommand]
	private async Task BrowseRemuxOutputAsync()
	{
		if (await AppLocator.Current.GetRequiredService<IStoragePickerService>().PickSaveVideoFileAsync(RemuxOutput) is { } path)
		{
			RemuxOutput = path;
		}
	}

	[ReactiveCommand(CanExecute = nameof(CanQueueRemux))]
	private async Task QueueRemuxAsync(CancellationToken cancellationToken)
	{
		await AppLocator.Current.GetRequiredService<IMediaToolAppService>().QueueRemuxAsync(new RemuxInput(RemuxInput.Trim(), RemuxOutput.Trim(), DeleteRemuxSource, RepairFlv), cancellationToken);
		NotifyQueued();
		RemuxInput = string.Empty;
	}

	[ReactiveCommand]
	private async Task BrowseCutInputAsync()
	{
		if (await AppLocator.Current.GetRequiredService<IStoragePickerService>().PickVideoFileAsync(GetDirectory(CutInput)) is { } path)
		{
			CutInput = path;
		}
	}

	[ReactiveCommand]
	private async Task BrowseCutOutputAsync()
	{
		if (await AppLocator.Current.GetRequiredService<IStoragePickerService>().PickSaveVideoFileAsync(CutOutput) is { } path)
		{
			CutOutput = path;
		}
	}

	[ReactiveCommand(CanExecute = nameof(CanQueueCut))]
	private async Task QueueCutAsync(CancellationToken cancellationToken)
	{
		if (!TryParseTime(CutStart, out TimeSpan start) || !TryParseTime(CutEnd, out TimeSpan end))
		{
			throw new FormatException(AppLanguage.Current.InvalidTimeRange);
		}

		string output = CutOutput.Trim();
		await AppLocator.Current.GetRequiredService<IMediaToolAppService>().QueueCutAsync(new CutInput(CutInput.Trim(), output, start, end), cancellationToken);
		NotifyQueued();

		// 排队的任务还没写出文件，下一个建议要跳过它
		_queuedCutOutputs.Add(Path.GetFullPath(output));
		CutOutput = SuggestCutOutput(CutInput, _queuedCutOutputs);
	}

	private void UpdateFFmpegVersionText()
	{
		FFmpegVersion = _ffmpegVersion is null ? null : AppLanguage.Current.FFmpegVersion.ToString(_ffmpegVersion);
	}

	private static bool CanQueue(bool? isFFmpegAvailable, string input, string output)
	{
		return isFFmpegAvailable is true && !string.IsNullOrWhiteSpace(input) && !string.IsNullOrWhiteSpace(output);
	}

	private static void NotifyQueued()
	{
		AppLocator.Current.GetRequiredService<INotificationService>().Show(AppLanguage.Current.Tasks, AppLanguage.Current.TaskAdded, AppNotificationType.Success);
	}

	private static bool TryParseTime(string text, out TimeSpan time)
	{
		return TimeSpan.TryParse(text.Trim(), CultureInfo.InvariantCulture, out time);
	}

	private static string? GetDirectory(string path)
	{
		return string.IsNullOrWhiteSpace(path) ? null : Path.GetDirectoryName(path);
	}

	internal static string SuggestRemuxOutput(string input)
	{
		string extension = Path.GetExtension(input);

		return RemuxableExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase) ? Path.ChangeExtension(input, ".mp4") : string.Empty;
	}

	internal static string SuggestCutOutput(string input, IReadOnlySet<string> reserved)
	{
		if (string.IsNullOrWhiteSpace(input))
		{
			return string.Empty;
		}

		string withoutExtension = Path.ChangeExtension(input, null);
		string extension = Path.GetExtension(input);

		for (int index = 1; ; ++index)
		{
			string candidate = string.Create(CultureInfo.InvariantCulture, $"{withoutExtension}_{index}{extension}");

			if (!File.Exists(candidate) && !reserved.Contains(Path.GetFullPath(candidate)))
			{
				return candidate;
			}
		}
	}
}
