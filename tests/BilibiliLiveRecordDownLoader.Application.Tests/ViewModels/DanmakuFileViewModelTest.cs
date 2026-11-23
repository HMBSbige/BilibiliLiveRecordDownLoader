using BilibiliLive.Danmu;
using BilibiliLiveRecordDownLoader.Application.Contracts.Danmaku;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using BilibiliLiveRecordDownLoader.Domain.Shared.Localization;
using BilibiliLiveRecordDownLoader.ViewModels.Danmaku;
using System.Globalization;
using System.Windows.Input;

namespace BilibiliLiveRecordDownLoader.Application.Tests.ViewModels;

public sealed class DanmakuFileViewModelTest
{
	private const string DanmakuPath = "room/20261122-200000_标题_001.xml";

	private static readonly DanmakuHighlightDto Highlight = new(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(70), TimeSpan.FromSeconds(80), "好耶");

	internal static DanmakuFileDto CreateFile(TimeSpan offset = default, string? video = "room/20261122-200000_标题_001.flv", bool isNative = true, params double[] seconds)
	{
		DanmakuFileSourceDto source = isNative
			? new DanmakuFileSourceDto.Native("主播", "标题", offset)
			: new DanmakuFileSourceDto.Standard();
		DanmakuFileEntryDto[] entries = [.. seconds.Select(static second => new DanmakuFileEntryDto(TimeSpan.FromSeconds(second), FakeDanmakuFeed.Comment(second.ToString(CultureInfo.InvariantCulture))))];

		return new DanmakuFileDto(DanmakuPath, source, entries, true, new DanmakuDensityDto(TimeSpan.FromSeconds(5), [1, 2, 3]), [Highlight], video);
	}

	[Test]
	[Arguments(0d, "10")]
	[Arguments(25d, "30")]
	[Arguments(30d, "30")]
	// 都在它之前时停在最后一条
	[Arguments(500d, "90")]
	public async Task JumpingSelectsTheFirstEntryNotBeforeTheTimeAsync(double seconds, string expected)
	{
		using DanmakuFileViewModel viewModel = new(CreateFile(seconds: [10, 20, 30, 90]), new FakeDanmakuAppService());

		((ICommand)viewModel.JumpToCommand).Execute(TimeSpan.FromSeconds(seconds));

		await Assert.That(viewModel.SelectedEntry?.Text).IsEqualTo(expected);
	}

	[Test]
	public async Task JumpingSkipsFilteredEntriesAsync()
	{
		DanmakuFileDto file = CreateFile(seconds: [10, 30]) with
		{
			Entries =
			[
				new DanmakuFileEntryDto(TimeSpan.FromSeconds(10), FakeDanmakuFeed.Comment("a")),
				new DanmakuFileEntryDto(TimeSpan.FromSeconds(20), new DanmakuEntryDto.Gift(new DanmuSender(1, "x", GuardLevel.None, null), "小花花", 1, 100)),
				new DanmakuFileEntryDto(TimeSpan.FromSeconds(30), FakeDanmakuFeed.Comment("b"))
			]
		};
		using DanmakuFileViewModel viewModel = new(file, new FakeDanmakuAppService());

		viewModel.Filter.ShowGifts = false;
		((ICommand)viewModel.JumpToCommand).Execute(TimeSpan.FromSeconds(15));

		await Assert.That(viewModel.SelectedEntry?.Text).IsEqualTo("b");
	}

	[Test]
	public async Task OnlyNativeFilesCanBeRewrittenAsync()
	{
		using DanmakuFileViewModel native = new(CreateFile(TimeSpan.FromSeconds(1.5), seconds: [1]), new FakeDanmakuAppService());
		using DanmakuFileViewModel standard = new(CreateFile(isNative: false, seconds: [1]), new FakeDanmakuAppService());

		using (Assert.Multiple())
		{
			await Assert.That((native.CanAdjustOffset, native.OffsetSeconds, native.Notice)).IsEqualTo((true, 1.5m, null));
			await Assert.That((standard.CanAdjustOffset, standard.Notice)).IsEqualTo((false, AppLanguage.Current.DanmakuFileReadOnly));
			await Assert.That(((ICommand)standard.RegenerateCommand).CanExecute(null)).IsFalse();
		}
	}

	public static IEnumerable<Func<(DanmakuOffsetResult Result, string Message, AppNotificationType Type)>> RewriteResults()
	{
		yield return static () => (new DanmakuOffsetResult.Updated(null), AppLanguage.Current.DanmakuOffsetSaved, AppNotificationType.Success);
		yield return static () => (new DanmakuOffsetResult.Updated("room/20261122-200000_标题_001.ass"), AppLanguage.Current.DanmakuSubtitleRegenerated, AppNotificationType.Success);
		yield return static () => (new DanmakuOffsetResult.SubtitleNotWritten("room/a.ass", "locked"), AppLanguage.Current.DanmakuSubtitleNotWritten.ToString("a.ass", "locked"), AppNotificationType.Warning);
	}

	[Test]
	[MethodDataSource(nameof(RewriteResults))]
	public async Task RegeneratingReloadsTheFileWithTheNewOffsetAsync(DanmakuOffsetResult result, string message, AppNotificationType type, CancellationToken cancellationToken)
	{
		FakeDanmakuAppService danmaku = new();
		TimeSpan? requested = null;
		danmaku.ChangeOffset = (_, offset) =>
		{
			requested = offset;
			return result;
		};
		danmaku.OpenFile = static (_, _) => Task.FromResult(CreateFile(TimeSpan.FromSeconds(2), seconds: [12, 22]));
		using DanmakuFileViewModel viewModel = new(CreateFile(seconds: [10, 20]), danmaku);

		viewModel.OffsetSeconds = 2;
		((ICommand)viewModel.RegenerateCommand).Execute(null);
		await WaitForMessageAsync(viewModel, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(requested).IsEqualTo(TimeSpan.FromSeconds(2));
			await Assert.That(viewModel.Entries.Select(static entry => entry.Text)).IsEquivalentTo(["12", "22"], CollectionOrdering.Matching);
			await Assert.That((viewModel.Message, viewModel.MessageType)).IsEqualTo((message, type));
		}
	}

	public static IEnumerable<Func<(Exception Failure, string Message)>> RewriteFailures()
	{
		yield return static () => (new IOException("正在录制"), "正在录制");
		yield return static () => (new InvalidDataException("不是本程序写的"), "不是本程序写的");
		yield return static () => (new UnauthorizedAccessException("只读"), "只读");
		yield return static () => (new FileNotFoundException(null, DanmakuPath), AppLanguage.Current.FileNotFound.ToString(DanmakuPath));
	}

	[Test]
	[MethodDataSource(nameof(RewriteFailures))]
	public async Task RewriteFailuresAreShownWithTheirReasonAsync(Exception failure, string message, CancellationToken cancellationToken)
	{
		FakeDanmakuAppService danmaku = new() { ChangeOffset = (_, _) => throw failure };
		using DanmakuFileViewModel viewModel = new(CreateFile(seconds: [1]), danmaku);

		((ICommand)viewModel.RegenerateCommand).Execute(null);
		await WaitForMessageAsync(viewModel, cancellationToken);

		await Assert.That((viewModel.Message, viewModel.MessageType)).IsEqualTo((message, AppNotificationType.Error));
	}

	[Test]
	public async Task ExportingAHighlightReportsWhereItGoesAsync(CancellationToken cancellationToken)
	{
		FakeDanmakuAppService danmaku = new();
		DanmakuHighlightDto? exported = null;
		danmaku.QueueHighlightExport = (_, highlight) =>
		{
			exported = highlight;
			return "room/20261122-200000_标题_001.highlight-000045.flv";
		};
		using DanmakuFileViewModel viewModel = new(CreateFile(seconds: [1]), danmaku);

		((ICommand)viewModel.ExportHighlightCommand).Execute(viewModel.Highlights.Single());
		await WaitForMessageAsync(viewModel, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(exported).IsEqualTo(Highlight);
			await Assert.That((viewModel.Message, viewModel.MessageType)).IsEqualTo((AppLanguage.Current.HighlightExportQueued.ToString("20261122-200000_标题_001.highlight-000045.flv"), AppNotificationType.Success));
		}
	}

	public static IEnumerable<Func<(Exception Failure, string Message)>> ExportFailures()
	{
		// 没有 FFmpeg 时剪辑抛 InvalidOperationException
		yield return static () => (new InvalidOperationException(AppLanguage.Current.FFmpegNotFound), AppLanguage.Current.FFmpegNotFound);
		// 视频在打开文件之后被移走时，应用服务说的是找不到同名视频，不能当成找不到弹幕文件
		yield return static () => (new FileNotFoundException(VideoNotFound, DanmakuPath), VideoNotFound);
	}

	[Test]
	[MethodDataSource(nameof(ExportFailures))]
	public async Task ExportFailuresAreShownWithTheirReasonAsync(Exception failure, string message, CancellationToken cancellationToken)
	{
		FakeDanmakuAppService danmaku = new() { QueueHighlightExport = (_, _) => throw failure };
		using DanmakuFileViewModel viewModel = new(CreateFile(seconds: [1]), danmaku);

		((ICommand)viewModel.ExportHighlightCommand).Execute(viewModel.Highlights.Single());
		await WaitForMessageAsync(viewModel, cancellationToken);

		await Assert.That((viewModel.Message, viewModel.MessageType)).IsEqualTo((message, AppNotificationType.Error));
	}

	[Test]
	public async Task ExportNeedsTheVideoWithTheSameNameAsync()
	{
		using DanmakuFileViewModel viewModel = new(CreateFile(video: null, seconds: [1]), new FakeDanmakuAppService());

		using (Assert.Multiple())
		{
			await Assert.That(viewModel.HasVideo).IsFalse();
			await Assert.That(((ICommand)viewModel.ExportHighlightCommand).CanExecute(viewModel.Highlights.Single())).IsFalse();
		}
	}

	[Test]
	public async Task HighlightWithoutAPhraseIsNamedGenericallyAsync()
	{
		DanmakuHighlightViewModel highlight = new(Highlight with { Phrase = "" });

		await Assert.That((highlight.Phrase, highlight.TimeText)).IsEqualTo((AppLanguage.Current.ChapterHighlightWithoutPhrase, "0:01:00"));
	}

	// 文件按时间排序，删除可能排在被删的 SC 之前
	[Test]
	public async Task SuperChatDeletionsMarkTheSuperChatInsteadOfBeingListedAsync()
	{
		DanmuSender sender = new(2, "老板", GuardLevel.None, null);
		DanmakuFileDto file = CreateFile() with
		{
			Entries =
			[
				new DanmakuFileEntryDto(TimeSpan.FromSeconds(10), new DanmakuEntryDto.SuperChatDeletion([7])),
				new DanmakuFileEntryDto(TimeSpan.FromSeconds(10), new DanmakuEntryDto.SuperChat(7, sender, "被删", 30_000, TimeSpan.FromMinutes(1))),
				new DanmakuFileEntryDto(TimeSpan.FromSeconds(20), new DanmakuEntryDto.SuperChat(8, sender, "留着", 30_000, TimeSpan.FromMinutes(1)))
			]
		};
		using DanmakuFileViewModel viewModel = new(file, new FakeDanmakuAppService());

		await Assert.That(viewModel.Entries.Select(static entry => (entry.Text, entry.IsDeleted))).IsEquivalentTo([("被删", true), ("留着", false)], CollectionOrdering.Matching);
	}

	private static string VideoNotFound => AppLanguage.Current.DanmakuVideoNotFound.ToString(Path.GetFileName(DanmakuPath));

	private static async Task WaitForMessageAsync(DanmakuFileViewModel viewModel, CancellationToken cancellationToken)
	{
		await TestWait.UntilAsync(() => viewModel.Message is not null && !viewModel.IsBusy, "the file has been read and a message is shown", cancellationToken);
	}
}
