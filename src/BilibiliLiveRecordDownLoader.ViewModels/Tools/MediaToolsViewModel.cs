namespace BilibiliLiveRecordDownLoader.ViewModels.Tools;

public sealed partial class MediaToolsViewModel : ViewModelBase
{
	// .mp4 只有本程序录的 fMP4 需要转换，由 GetMp4ConversionPath 识别
	private static readonly string[] RemuxableExtensions = [".flv", ".ts", ".mkv", ".m4s", ".mp4"];

	private static readonly string[] TimeFormats = [@"h\:mm\:ss\.FFF", @"h\:mm\:ss", @"m\:ss\.FFF", @"m\:ss"];

	// 用户配错 FFmpeg 路径时，指向的程序可能不会自行退出
	private static readonly TimeSpan FFmpegCheckTimeout = TimeSpan.FromSeconds(10);

	// Windows、macOS 的路径默认不区分大小写
	private static readonly StringComparer PathComparer = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

	private readonly IMediaToolAppService _mediaTools;
	private readonly IBackgroundTaskAppService _tasks;
	private readonly INotificationService _notifications;
	private string? _ffmpegVersion;
	private int _ffmpegCheckGeneration;

	public MediaToolsViewModel(IMediaToolAppService mediaTools, IBackgroundTaskAppService tasks, INotificationService notifications)
	{
		_mediaTools = mediaTools;
		_tasks = tasks;
		_notifications = notifications;

		CanQueueRemux = this.WhenChanged(static viewModel => viewModel.IsFFmpegAvailable, static viewModel => viewModel.RemuxInput, static viewModel => viewModel.RemuxOutput, CanQueue);
		CanQueueCut = this.WhenChanged
		(
			static viewModel => viewModel.IsFFmpegAvailable,
			static viewModel => viewModel.CutInput,
			static viewModel => viewModel.CutOutput,
			static viewModel => viewModel.CutStart,
			static viewModel => viewModel.CutEnd,
			static (isFFmpegAvailable, input, output, start, end) => CanQueue(isFFmpegAvailable, input, output) && TryParseTimeRange(start, end, out _, out _)
		);

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
			.Subscribe(input => RemuxOutput = SuggestRemuxOutput(input, GetPendingOutputs()))
			.DisposeWith(Disposables);

		this.WhenChanged(static viewModel => viewModel.CutInput)
			.Skip(1)
			.Subscribe(input => CutOutput = SuggestCutOutput(input, GetPendingOutputs()))
			.DisposeWith(Disposables);

		this.WhenChanged(static viewModel => viewModel.RemuxInput, static viewModel => viewModel.RemuxOutput, static (_, _) => RxVoid.Default)
			.Subscribe(_ => RemuxError = null)
			.DisposeWith(Disposables);

		this.WhenChanged
			(
				static viewModel => viewModel.CutInput,
				static viewModel => viewModel.CutOutput,
				static viewModel => viewModel.CutStart,
				static viewModel => viewModel.CutEnd,
				static string? (_, _, start, end) => TryParseTimeRange(start, end, out _, out _) ? null : AppLanguage.Current.InvalidTimeRange
			)
			.Subscribe(error => CutError = error)
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
	public partial string? RemuxError { get; private set; }

	[Reactive]
	public partial string CutInput { get; set; } = string.Empty;

	[Reactive]
	public partial string CutOutput { get; set; } = string.Empty;

	[Reactive]
	public partial string CutStart { get; set; } = "00:00:00.000";

	[Reactive]
	public partial string CutEnd { get; set; } = "01:00:00.000";

	[Reactive]
	public partial string? CutError { get; private set; }

	internal void RefreshFFmpegStatus()
	{
		Forget(CheckFFmpegAsync);
	}

	[ReactiveCommand]
	private async Task CheckFFmpegAsync(CancellationToken cancellationToken)
	{
		int generation = ++_ffmpegCheckGeneration;
		string? version = null;
		IsFFmpegAvailable = null;

		try
		{
			using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			timeout.CancelAfter(FFmpegCheckTimeout);
			version = await _mediaTools.GetFFmpegVersionAsync(timeout.Token);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			// 超时按不可用处理
		}
		finally
		{
			// 切换页面和点“重新检查”都会发起检查，以最后一次为准
			if (generation == _ffmpegCheckGeneration)
			{
				_ffmpegVersion = version;
				UpdateFFmpegVersionText();
				IsFFmpegAvailable = version is not null;
			}
		}
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
		try
		{
			await _mediaTools.QueueRemuxAsync(new RemuxInput(RemuxInput.Trim(), RemuxOutput.Trim(), DeleteRemuxSource, RepairFlv ? new RemuxMode.RepairFlv() : new RemuxMode.Plain()), cancellationToken);
		}
		catch (Exception exception) when (IsInputError(exception))
		{
			RemuxError = exception.Message;
			return;
		}

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
		if (!TryParseTimeRange(CutStart, CutEnd, out TimeSpan start, out TimeSpan end))
		{
			return;
		}

		try
		{
			await _mediaTools.QueueCutAsync(new CutInput(CutInput.Trim(), CutOutput.Trim(), start, end), cancellationToken);
		}
		catch (Exception exception) when (IsInputError(exception))
		{
			CutError = exception.Message;
			return;
		}

		NotifyQueued();
		CutOutput = SuggestCutOutput(CutInput, GetPendingOutputs());
	}

	private void UpdateFFmpegVersionText()
	{
		FFmpegVersion = _ffmpegVersion is null ? null : AppLanguage.Current.FFmpegVersion.ToString(_ffmpegVersion);
	}

	private HashSet<string> GetPendingOutputs()
	{
		IEnumerable<string> outputs = _tasks.GetTasks().Where(static task => task.Status is BackgroundTaskStatus.Queued or BackgroundTaskStatus.Running).Select(static task => task.TargetPath).OfType<string>();
		return new HashSet<string>(outputs.Select(Path.GetFullPath), PathComparer);
	}

	private void NotifyQueued()
	{
		_notifications.Show(AppLanguage.Current.Tasks, AppLanguage.Current.TaskAdded, AppNotificationType.Success);
	}

	private static bool CanQueue(bool? isFFmpegAvailable, string input, string output)
	{
		return isFFmpegAvailable is true && !string.IsNullOrWhiteSpace(input) && !string.IsNullOrWhiteSpace(output);
	}

	// 应用服务对输入问题抛出这两类异常
	private static bool IsInputError(Exception exception)
	{
		return exception is ArgumentException or FileNotFoundException;
	}

	internal static bool TryParseTimeRange(string startText, string endText, out TimeSpan start, out TimeSpan end)
	{
		end = default;
		return TryParseTime(startText, out start) && TryParseTime(endText, out end) && end > start;
	}

	// 宽松解析会把纯数字当作天数
	private static bool TryParseTime(string text, out TimeSpan time)
	{
		return TimeSpan.TryParseExact(text.Trim(), TimeFormats, CultureInfo.InvariantCulture, out time);
	}

	private static string? GetDirectory(string path)
	{
		return string.IsNullOrWhiteSpace(path) ? null : Path.GetDirectoryName(path);
	}

	internal static string SuggestRemuxOutput(string input, IReadOnlySet<string> reserved)
	{
		input = input.Trim();

		if (!RemuxableExtensions.Contains(Path.GetExtension(input), StringComparer.OrdinalIgnoreCase) || RecordingFileLayout.GetMp4ConversionPath(input) is not { } output)
		{
			return string.Empty;
		}

		return Numbered(output).Prepend(output).First(candidate => IsAvailable(candidate, reserved));
	}

	internal static string SuggestCutOutput(string input, IReadOnlySet<string> reserved)
	{
		input = input.Trim();

		return input.Length is 0 ? string.Empty : Numbered(input).First(candidate => IsAvailable(candidate, reserved));
	}

	// 排队的任务还没写出文件，建议的输出也要跳过它们
	private static bool IsAvailable(string path, IReadOnlySet<string> reserved)
	{
		return !File.Exists(path) && !reserved.Contains(Path.GetFullPath(path));
	}

	private static IEnumerable<string> Numbered(string path)
	{
		string withoutExtension = Path.ChangeExtension(path, null);
		string extension = Path.GetExtension(path);

		for (int index = 1; ; ++index)
		{
			yield return string.Create(CultureInfo.InvariantCulture, $"{withoutExtension}_{index}{extension}");
		}
	}
}
