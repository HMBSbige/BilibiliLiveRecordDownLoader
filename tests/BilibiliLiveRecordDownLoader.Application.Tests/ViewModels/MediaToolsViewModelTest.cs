using BilibiliLiveRecordDownLoader.Application.Contracts.Media;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using BilibiliLiveRecordDownLoader.Domain.Shared.Localization;
using BilibiliLiveRecordDownLoader.ViewModels.Tools;
using System.Collections.Concurrent;
using System.Windows.Input;

namespace BilibiliLiveRecordDownLoader.Application.Tests.ViewModels;

public sealed class MediaToolsViewModelTest
{
	private static readonly HashSet<string> NothingReserved = [];

	[Test]
	public async Task RemuxOutputSkipsExistingAndQueuedFilesAsync()
	{
		using TestDirectory directory = new();
		await File.WriteAllTextAsync(directory.Combine("a.mp4"), "user");
		HashSet<string> reserved = [Path.GetFullPath(directory.Combine("a_1.mp4"))];

		string suggested = MediaToolsViewModel.SuggestRemuxOutput(directory.Combine("a.flv"), reserved);

		await Assert.That(suggested).IsEqualTo(directory.Combine("a_2.mp4"));
	}

	[Test]
	[Arguments("a.flv", "a.mp4")]
	[Arguments("a.TS", "a.mp4")]
	[Arguments("20260102_030405_fmp4.mp4", "20260102_030405.mp4")]
	[Arguments("a.mp4", null)]
	[Arguments("a.txt", null)]
	public async Task RemuxOutputFollowsTheRecordingConversionRuleAsync(string fileName, string? expected)
	{
		using TestDirectory directory = new();

		string suggested = MediaToolsViewModel.SuggestRemuxOutput(directory.Combine(fileName), NothingReserved);

		await Assert.That(suggested).IsEqualTo(expected is null ? string.Empty : directory.Combine(expected));
	}

	[Test]
	public async Task CutOutputSkipsExistingAndQueuedFilesAsync()
	{
		using TestDirectory directory = new();
		await File.WriteAllTextAsync(directory.Combine("a_1.flv"), "user");
		HashSet<string> reserved = [Path.GetFullPath(directory.Combine("a_2.flv"))];

		string suggested = MediaToolsViewModel.SuggestCutOutput(directory.Combine("a.flv"), reserved);

		await Assert.That(suggested).IsEqualTo(directory.Combine("a_3.flv"));
	}

	[Test]
	[Timeout(30_000)]
	public async Task QueuedRemuxOutputIsNotSuggestedAgainAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		FakeMediaTools tools = new();
		using MediaToolsViewModel viewModel = await CreateWithFFmpegAsync(tools, new RecordingNotifications(), cancellationToken);

		viewModel.RemuxInput = directory.Combine("a.flv");
		string first = viewModel.RemuxOutput;
		((ICommand)viewModel.QueueRemuxCommand).Execute(null);
		await TestWait.UntilAsync(() => viewModel.RemuxInput.Length is 0, "the queued remux clears its input", cancellationToken);
		viewModel.RemuxInput = directory.Combine("a.flv");

		using (Assert.Multiple())
		{
			await Assert.That(first).IsEqualTo(directory.Combine("a.mp4"));
			await Assert.That(tools.Remuxes.Single().Destination).IsEqualTo(first);
			await Assert.That(viewModel.RemuxOutput).IsEqualTo(directory.Combine("a_1.mp4"));
		}
	}

	// 结束了的任务不再占用
	[Test]
	[Timeout(30_000)]
	public async Task OutputsOfOtherPendingTasksAreNotSuggestedAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		FakeTasks tasks = new();
		tasks.Add(directory.Combine("a.mp4"), BackgroundTaskStatus.Queued);
		tasks.Add(directory.Combine("a_1.mp4"), BackgroundTaskStatus.Running);
		tasks.Add(directory.Combine("a_2.mp4"), BackgroundTaskStatus.Failed);
		tasks.Add(directory.Combine("a_1.flv"), BackgroundTaskStatus.Queued);
		using MediaToolsViewModel viewModel = await CreateWithFFmpegAsync(new FakeMediaTools(tasks), new RecordingNotifications(), cancellationToken);

		viewModel.RemuxInput = directory.Combine("a.flv");
		viewModel.CutInput = directory.Combine("a.flv");

		await Assert.That((viewModel.RemuxOutput, viewModel.CutOutput)).IsEqualTo((directory.Combine("a_2.mp4"), directory.Combine("a_2.flv")));
	}

	[Test]
	[Arguments("00:00:00.000", "01:00:00.000", 0d, 3600d)]
	[Arguments("1:30", "2:00", 90d, 120d)]
	[Arguments("0:00:10.5", "00:01:00", 10.5d, 60d)]
	// 不分段的录像可能超过一天，只给分和秒时分钟也可能超过一小时
	[Arguments("25:00:00", "25:10:00", 90_000d, 90_600d)]
	[Arguments("123:59:59.5", "124:00:00", 446_399.5d, 446_400d)]
	[Arguments("90:00", "1500:00.5", 5400d, 90_000.5d)]
	public async Task CutTimesAreReadInTheShownFormatAsync(string start, string end, double startSeconds, double endSeconds)
	{
		bool parsed = MediaToolsViewModel.TryParseTimeRange(start, end, out TimeSpan parsedStart, out TimeSpan parsedEnd);

		await Assert.That((parsed, parsedStart, parsedEnd)).IsEqualTo((true, TimeSpan.FromSeconds(startSeconds), TimeSpan.FromSeconds(endSeconds)));
	}

	[Test]
	// 宽松解析会把纯数字当作天数
	[Arguments("0", "90")]
	[Arguments("00:02:00", "00:01:00")]
	[Arguments("00:01:00", "abc")]
	[Arguments("1:60:00", "2:00:00")]
	[Arguments("1:00", "1:60")]
	public async Task InvalidCutTimesAreReportedBeforeQueuingAsync(string start, string end)
	{
		using MediaToolsViewModel viewModel = Create(new FakeMediaTools(), new RecordingNotifications());
		string? before = viewModel.CutError;

		viewModel.CutStart = start;
		viewModel.CutEnd = end;

		using (Assert.Multiple())
		{
			await Assert.That(before).IsNull();
			await Assert.That(viewModel.CutError).IsEqualTo(AppLanguage.Current.InvalidTimeRange);
			await Assert.That(MediaToolsViewModel.TryParseTimeRange(start, end, out _, out _)).IsFalse();
		}
	}

	[Test]
	[Timeout(30_000)]
	public async Task InputErrorsAreShownInTheSectionInsteadOfAsNotificationsAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		FakeMediaTools tools = new() { CutFailure = new FileNotFoundException("文件不存在") };
		RecordingNotifications notifications = new();
		using MediaToolsViewModel viewModel = await CreateWithFFmpegAsync(tools, notifications, cancellationToken);

		viewModel.CutInput = directory.Combine("a.flv");
		((ICommand)viewModel.QueueCutCommand).Execute(null);
		await TestWait.UntilAsync(() => viewModel.CutError is not null, "the cut error is shown", cancellationToken);
		string? shown = viewModel.CutError;
		viewModel.CutOutput = directory.Combine("b.flv");

		using (Assert.Multiple())
		{
			await Assert.That(shown).IsEqualTo("文件不存在");
			await Assert.That(notifications.Shown).IsEmpty();
			await Assert.That(viewModel.CutError).IsNull();
		}
	}

	[Test]
	[Timeout(30_000)]
	public async Task FFmpegCheckThatTimesOutShowsFFmpegAsUnavailableAsync(CancellationToken cancellationToken)
	{
		FakeMediaTools tools = new() { GetVersion = static _ => Task.FromException<string?>(new OperationCanceledException()) };
		using MediaToolsViewModel viewModel = Create(tools, new RecordingNotifications());

		viewModel.RefreshFFmpegStatus();
		await TestWait.UntilAsync(() => viewModel.IsFFmpegAvailable is not null, "the FFmpeg status is known", cancellationToken);

		await Assert.That(viewModel.IsFFmpegAvailable).IsFalse();
	}

	private static MediaToolsViewModel Create(FakeMediaTools tools, RecordingNotifications notifications)
	{
		return new MediaToolsViewModel(tools, tools.Tasks, notifications);
	}

	private static async Task<MediaToolsViewModel> CreateWithFFmpegAsync(FakeMediaTools tools, RecordingNotifications notifications, CancellationToken cancellationToken)
	{
		MediaToolsViewModel viewModel = Create(tools, notifications);
		viewModel.RefreshFFmpegStatus();
		await TestWait.UntilAsync(() => viewModel.IsFFmpegAvailable is true, "FFmpeg is shown as available", cancellationToken);
		return viewModel;
	}

	// 和真实的服务一样，排进去的任务立即出现在任务列表里
	private sealed class FakeMediaTools(FakeTasks? tasks = null) : IMediaToolAppService
	{
		public FakeTasks Tasks { get; } = tasks ?? new FakeTasks();

		public Func<CancellationToken, Task<string?>> GetVersion { get; init; } = static _ => Task.FromResult<string?>("7.1");

		public Exception? CutFailure { get; init; }

		public ConcurrentQueue<RemuxInput> Remuxes { get; } = new();

		public Task<string?> GetFFmpegVersionAsync(CancellationToken cancellationToken = default)
		{
			return GetVersion(cancellationToken);
		}

		public Task<Task<BackgroundTaskSnapshot>> QueueRemuxAsync(RemuxInput input, CancellationToken cancellationToken = default)
		{
			Remuxes.Enqueue(input);
			Tasks.Add(input.Destination, BackgroundTaskStatus.Queued);
			return Task.FromResult(new TaskCompletionSource<BackgroundTaskSnapshot>().Task);
		}

		public Task QueueCutAsync(CutInput input, CancellationToken cancellationToken = default)
		{
			if (CutFailure is { } failure)
			{
				return Task.FromException(failure);
			}

			Tasks.Add(input.Destination, BackgroundTaskStatus.Queued);
			return Task.CompletedTask;
		}
	}

	private sealed class FakeTasks : IBackgroundTaskAppService
	{
		private readonly ConcurrentQueue<BackgroundTaskSnapshot> _tasks = new();

		public long Version => _tasks.Count;

		public bool HasActiveTasks => !_tasks.IsEmpty;

		public void Add(string targetPath, BackgroundTaskStatus status)
		{
			_tasks.Enqueue(new BackgroundTaskSnapshot(Guid.NewGuid(), Path.GetFileName(targetPath), null, targetPath, status, null, null, null));
		}

		public IReadOnlyList<BackgroundTaskSnapshot> GetTasks()
		{
			return [.. _tasks];
		}

		public void Cancel(Guid id)
		{
		}

		public void CancelAll()
		{
		}

		public void RemoveFinished()
		{
		}
	}
}
