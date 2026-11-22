using BilibiliLive.Danmu;
using BilibiliLiveRecordDownLoader.Application.Contracts.Danmaku;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using BilibiliLiveRecordDownLoader.Domain.Shared.Localization;
using BilibiliLiveRecordDownLoader.ViewModels.Danmaku;
using System.Windows.Input;
using TUnit.Assertions.Enums;

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
		DanmakuFileEntryDto[] entries = [.. seconds.Select(static second => new DanmakuFileEntryDto(TimeSpan.FromSeconds(second), FakeDanmakuFeed.Comment(second.ToString(System.Globalization.CultureInfo.InvariantCulture))))];

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
			await Assert.That((standard.CanAdjustOffset, standard.Notice)).IsEqualTo((false, (string?)AppLanguage.Current.DanmakuFileReadOnly));
			await Assert.That(((ICommand)standard.RegenerateCommand).CanExecute(null)).IsFalse();
		}
	}

	[Test]
	[Timeout(30_000)]
	public async Task RegeneratingReloadsTheFileWithTheNewOffsetAsync(CancellationToken cancellationToken)
	{
		FakeDanmakuAppService danmaku = new();
		TimeSpan? requested = null;
		danmaku.ChangeOffset = (_, offset) =>
		{
			requested = offset;
			return new DanmakuOffsetResult.Updated("room/20261122-200000_标题_001.ass");
		};
		danmaku.OpenFile = _ => CreateFile(TimeSpan.FromSeconds(2), seconds: [12, 22]);
		using DanmakuFileViewModel viewModel = new(CreateFile(seconds: [10, 20]), danmaku);

		viewModel.OffsetSeconds = 2;
		((ICommand)viewModel.RegenerateCommand).Execute(null);
		await WaitForMessageAsync(viewModel, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(requested).IsEqualTo(TimeSpan.FromSeconds(2));
			await Assert.That(viewModel.Entries.Select(static entry => entry.Text).ToArray()).IsEquivalentTo(["12", "22"], EqualityComparer<string>.Default, CollectionOrdering.Matching);
			await Assert.That((viewModel.Message, viewModel.MessageType)).IsEqualTo(((string?)AppLanguage.Current.DanmakuSubtitleRegenerated, AppNotificationType.Success));
		}
	}

	[Test]
	[Timeout(30_000)]
	public async Task SubtitleNotWrittenShowsTheReasonAsync(CancellationToken cancellationToken)
	{
		FakeDanmakuAppService danmaku = new()
		{
			ChangeOffset = static (_, _) => new DanmakuOffsetResult.SubtitleNotWritten("room/a.ass", "locked"),
			OpenFile = static _ => CreateFile(seconds: [1])
		};
		using DanmakuFileViewModel viewModel = new(CreateFile(seconds: [1]), danmaku);

		((ICommand)viewModel.RegenerateCommand).Execute(null);
		await WaitForMessageAsync(viewModel, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That((viewModel.Message, viewModel.MessageType)).IsEqualTo(((string?)AppLanguage.Current.DanmakuSubtitleNotWritten.ToString("a.ass", "locked"), AppNotificationType.Warning));
			await Assert.That(viewModel.IsBusy).IsFalse();
		}
	}

	public static IEnumerable<Func<Exception>> RewriteFailures()
	{
		yield return static () => new IOException("正在录制");
		yield return static () => new InvalidDataException("不是本程序写的");
		yield return static () => new UnauthorizedAccessException("只读");
	}

	[Test]
	[Timeout(30_000)]
	[MethodDataSource(nameof(RewriteFailures))]
	public async Task RewriteFailuresAreShownWithTheirReasonAsync(Exception failure, CancellationToken cancellationToken)
	{
		FakeDanmakuAppService danmaku = new() { ChangeOffset = (_, _) => throw failure };
		using DanmakuFileViewModel viewModel = new(CreateFile(seconds: [1]), danmaku);

		((ICommand)viewModel.RegenerateCommand).Execute(null);
		await WaitForMessageAsync(viewModel, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That((viewModel.Message, viewModel.MessageType)).IsEqualTo(((string?)failure.Message, AppNotificationType.Error));
			await Assert.That(viewModel.IsBusy).IsFalse();
		}
	}

	[Test]
	[Timeout(30_000)]
	public async Task ExportingAHighlightReportsWhereItGoesAsync(CancellationToken cancellationToken)
	{
		FakeDanmakuAppService danmaku = new();
		DanmakuHighlightDto? exported = null;
		danmaku.QueueHighlightExport = (path, highlight) =>
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
			await Assert.That((viewModel.Message, viewModel.MessageType)).IsEqualTo(((string?)AppLanguage.Current.HighlightExportQueued.ToString("20261122-200000_标题_001.highlight-000045.flv"), AppNotificationType.Success));
		}
	}

	// 没有 FFmpeg 时剪辑抛 InvalidOperationException
	[Test]
	[Timeout(30_000)]
	public async Task ExportFailuresAreShownWithTheirReasonAsync(CancellationToken cancellationToken)
	{
		FakeDanmakuAppService danmaku = new() { QueueHighlightExport = static (_, _) => throw new InvalidOperationException(AppLanguage.Current.FFmpegNotFound) };
		using DanmakuFileViewModel viewModel = new(CreateFile(seconds: [1]), danmaku);

		((ICommand)viewModel.ExportHighlightCommand).Execute(viewModel.Highlights.Single());
		await WaitForMessageAsync(viewModel, cancellationToken);

		await Assert.That((viewModel.Message, viewModel.MessageType)).IsEqualTo(((string?)AppLanguage.Current.FFmpegNotFound, AppNotificationType.Error));
	}

	// 视频在打开文件之后被移走时，应用服务说的是找不到同名视频
	[Test]
	[Timeout(30_000)]
	public async Task MissingVideoIsNotReportedAsAMissingDanmakuFileAsync(CancellationToken cancellationToken)
	{
		string reason = AppLanguage.Current.DanmakuVideoNotFound.ToString(Path.GetFileName(DanmakuPath));
		FakeDanmakuAppService danmaku = new() { QueueHighlightExport = (path, _) => throw new FileNotFoundException(reason, path) };
		using DanmakuFileViewModel viewModel = new(CreateFile(seconds: [1]), danmaku);

		((ICommand)viewModel.ExportHighlightCommand).Execute(viewModel.Highlights.Single());
		await WaitForMessageAsync(viewModel, cancellationToken);

		await Assert.That((viewModel.Message, viewModel.MessageType)).IsEqualTo(((string?)reason, AppNotificationType.Error));
	}

	[Test]
	[Timeout(30_000)]
	public async Task MissingDanmakuFileIsReportedWithItsPathAsync(CancellationToken cancellationToken)
	{
		FakeDanmakuAppService danmaku = new() { ChangeOffset = static (path, _) => throw new FileNotFoundException(null, path) };
		using DanmakuFileViewModel viewModel = new(CreateFile(seconds: [1]), danmaku);

		((ICommand)viewModel.RegenerateCommand).Execute(null);
		await WaitForMessageAsync(viewModel, cancellationToken);

		await Assert.That((viewModel.Message, viewModel.MessageType)).IsEqualTo(((string?)AppLanguage.Current.FileNotFound.ToString(DanmakuPath), AppNotificationType.Error));
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

		using (Assert.Multiple())
		{
			await Assert.That(highlight.Phrase).IsEqualTo(AppLanguage.Current.ChapterHighlightWithoutPhrase);
			await Assert.That(highlight.TimeText).IsEqualTo("0:01:00");
		}
	}

	private static async Task WaitForMessageAsync(DanmakuFileViewModel viewModel, CancellationToken cancellationToken)
	{
		await TestWait.UntilAsync(() => viewModel.Message is not null && !viewModel.IsBusy, "the file has been read and a message is shown", cancellationToken);
	}

	[Test]
	public async Task SuperChatDeletionsMarkTheSuperChatInsteadOfBeingListedAsync()
	{
		DanmuSender sender = new(2, "老板", GuardLevel.None, null);
		DanmakuFileDto file = CreateFile() with
		{
			Entries =
			[
				new DanmakuFileEntryDto(TimeSpan.FromSeconds(10), new DanmakuEntryDto.SuperChat(7, sender, "被删", 30_000, TimeSpan.FromMinutes(1))),
				new DanmakuFileEntryDto(TimeSpan.FromSeconds(20), new DanmakuEntryDto.SuperChat(8, sender, "留着", 30_000, TimeSpan.FromMinutes(1))),
				new DanmakuFileEntryDto(TimeSpan.FromSeconds(30), new DanmakuEntryDto.SuperChatDeletion([7]))
			]
		};
		using DanmakuFileViewModel viewModel = new(file, new FakeDanmakuAppService());

		using (Assert.Multiple())
		{
			await Assert.That(viewModel.Entries.Select(static entry => entry.Text).ToArray()).IsEquivalentTo(["被删", "留着"], EqualityComparer<string>.Default, CollectionOrdering.Matching);
			await Assert.That(viewModel.Entries.Select(static entry => entry.IsDeleted).ToArray()).IsEquivalentTo([true, false], EqualityComparer<bool>.Default, CollectionOrdering.Matching);
		}
	}
}
