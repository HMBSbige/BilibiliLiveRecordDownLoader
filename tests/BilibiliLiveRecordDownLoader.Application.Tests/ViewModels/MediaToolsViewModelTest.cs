using BilibiliLiveRecordDownLoader.Application.Contracts.Media;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using BilibiliLiveRecordDownLoader.Domain.Shared.Localization;
using BilibiliLiveRecordDownLoader.ViewModels.Tools;
using System.Collections.Concurrent;
using System.Globalization;
using System.Windows.Input;

namespace BilibiliLiveRecordDownLoader.Application.Tests.ViewModels;

public sealed class MediaToolsViewModelTest
{
	[Test]
	public async Task OutputsSuggestedByTheServiceAreShownForTheTrimmedInputAsync()
	{
		FakeMediaTools tools = new() { SuggestRemux = static source => source is "a.flv" ? "a.mp4" : null, SuggestCut = static source => source is "a.flv" ? "a_1.flv" : null };
		using MediaToolsViewModel viewModel = Create(tools, new RecordingNotifications());

		viewModel.RemuxInput = " a.flv ";
		viewModel.CutInput = " a.flv ";
		(string remux, string cut) = (viewModel.RemuxOutput, viewModel.CutOutput);
		viewModel.RemuxInput = "a.txt";

		await Assert.That((remux, cut, viewModel.RemuxOutput)).IsEqualTo(("a.mp4", "a_1.flv", string.Empty));
	}

	[Test]
	public async Task QueuedRemuxUsesTheSuggestedOutputAndClearsTheInputAsync(CancellationToken cancellationToken)
	{
		FakeMediaTools tools = new();
		using MediaToolsViewModel viewModel = await CreateWithFFmpegAsync(tools, new RecordingNotifications(), cancellationToken);

		viewModel.RemuxInput = "a.flv";
		string suggested = viewModel.RemuxOutput;
		((ICommand)viewModel.QueueRemuxCommand).Execute(null);
		await TestWait.UntilAsync(() => viewModel.RemuxInput.Length is 0, "the queued remux clears its input", cancellationToken);
		RemuxInput queued = await Assert.That(tools.Remuxes).HasSingleItem();

		await Assert.That(queued.Destination).IsEqualTo(suggested);
	}

	[Test]
	public async Task QueuedCutGetsTheNextOutputSuggestedAsync(CancellationToken cancellationToken)
	{
		int suggestions = 0;
		FakeMediaTools tools = new() { SuggestCut = source => string.Create(CultureInfo.InvariantCulture, $"{source}.{++suggestions}") };
		using MediaToolsViewModel viewModel = await CreateWithFFmpegAsync(tools, new RecordingNotifications(), cancellationToken);

		viewModel.CutInput = "a.flv";
		((ICommand)viewModel.QueueCutCommand).Execute(null);
		await TestWait.UntilAsync(() => viewModel.CutOutput is "a.flv.2", "the next output is suggested", cancellationToken);
		CutInput queued = await Assert.That(tools.Cuts).HasSingleItem();

		await Assert.That(queued.Destination).IsEqualTo("a.flv.1");
	}

	[Test]
	[Arguments("00:00:00.000", "01:00:00.000", 0d, 3600d)]
	[Arguments("1:30", "2:00", 90d, 120d)]
	[Arguments("0:00:10.5", "00:01:00", 10.5d, 60d)]
	[Arguments("25:00:00", "25:10:00", 90_000d, 90_600d)]
	[Arguments("90:00", "1500:00.5", 5400d, 90_000.5d)]
	public async Task CutTimesAreReadInTheShownFormatAsync(string start, string end, double startSeconds, double endSeconds)
	{
		bool parsed = MediaToolsViewModel.TryParseTimeRange(start, end, out TimeSpan parsedStart, out TimeSpan parsedEnd);

		await Assert.That((parsed, parsedStart, parsedEnd)).IsEqualTo((true, TimeSpan.FromSeconds(startSeconds), TimeSpan.FromSeconds(endSeconds)));
	}

	[Test]
	[Arguments("0", "90")]
	[Arguments("00:02:00", "00:01:00")]
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
		PlatformStub platform = new();
		return new MediaToolsViewModel(tools, notifications, platform, platform);
	}

	private static async Task<MediaToolsViewModel> CreateWithFFmpegAsync(FakeMediaTools tools, RecordingNotifications notifications, CancellationToken cancellationToken)
	{
		MediaToolsViewModel viewModel = Create(tools, notifications);
		viewModel.RefreshFFmpegStatus();
		await TestWait.UntilAsync(() => viewModel.IsFFmpegAvailable is true, "FFmpeg is shown as available", cancellationToken);
		return viewModel;
	}

	private sealed class FakeMediaTools : IMediaToolAppService
	{
		public Func<CancellationToken, Task<string?>> GetVersion { get; init; } = static _ => Task.FromResult<string?>("7.1");

		public Func<string, string?> SuggestRemux { get; init; } = static source => source + ".mp4";

		public Func<string, string?> SuggestCut { get; init; } = static source => source + ".cut";

		public Exception? CutFailure { get; init; }

		public ConcurrentQueue<RemuxInput> Remuxes { get; } = new();

		public ConcurrentQueue<CutInput> Cuts { get; } = new();

		public Task<string?> GetFFmpegVersionAsync(CancellationToken cancellationToken = default)
		{
			return GetVersion(cancellationToken);
		}

		public Task<Task<BackgroundTaskSnapshot>> QueueRemuxAsync(RemuxInput input, CancellationToken cancellationToken = default)
		{
			Remuxes.Enqueue(input);
			return Task.FromResult(new TaskCompletionSource<BackgroundTaskSnapshot>().Task);
		}

		public Task QueueCutAsync(CutInput input, CancellationToken cancellationToken = default)
		{
			if (CutFailure is { } failure)
			{
				return Task.FromException(failure);
			}

			Cuts.Enqueue(input);
			return Task.CompletedTask;
		}

		public string? SuggestRemuxDestination(string source)
		{
			return SuggestRemux(source);
		}

		public string? SuggestCutDestination(string source)
		{
			return SuggestCut(source);
		}
	}
}
