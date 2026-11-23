using BilibiliLive.Danmu;
using BilibiliLiveRecordDownLoader.Application.Contracts.Danmaku;
using BilibiliLiveRecordDownLoader.Application.Contracts.Media;
using BilibiliLiveRecordDownLoader.Application.Contracts.Platform;
using BilibiliLiveRecordDownLoader.Application.Danmaku;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using TUnit.Assertions.Enums;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Danmaku;

public sealed class DanmakuAppServiceTest
{
	// 前后各留 15 秒，剪的是 3710 到 3755 秒，文件名按开头记作 010150
	private static readonly DanmakuHighlightDto Highlight = new(TimeSpan.FromSeconds(3725), TimeSpan.FromSeconds(3730), TimeSpan.FromSeconds(3740), "好耶");

	private static ServiceProvider CreateServices(CutRecorder? cuts = null, AppConfig? config = null, IBackgroundTaskAppService? tasks = null)
	{
		ServiceCollection services = new();

		if (tasks is not null)
		{
			services.AddSingleton(tasks);
		}

		return services
			.AddLogging()
			.AddSingleton<TimeProvider>(new FakeTimeProvider())
			.AddSingleton<IAppConfigManager>(new FakeConfigManager(config))
			.AddSingleton<INotificationService, RecordingNotifications>()
			.AddSingleton<IMediaToolAppService>(cuts ?? new CutRecorder())
			.AddApplication()
			.BuildServiceProvider();
	}

	private static async Task<string> WriteDanmakuAsync(TestDirectory directory, IEnumerable<DanmakuRecord> records, CancellationToken cancellationToken, string name = "20260102_030405.xml")
	{
		string path = directory.Combine(name);
		await File.WriteAllBytesAsync(path, await DanmakuSamples.WriteAsync(records, cancellationToken), cancellationToken);
		return path;
	}

	private static byte[] Flv(int width, int height)
	{
		return [.. FlvSamples.Header, .. FlvSamples.MetadataWithSize(width, height)];
	}

	[Test]
	public async Task OpeningAFileGivesEntriesDensityHighlightsAndTheVideoAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		await using ServiceProvider services = CreateServices();
		List<DanmakuRecord> records = [.. Enumerable.Range(0, 600).Select(static i => (DanmakuRecord)DanmakuSamples.Comment(i * 3, $@"日常{i}", i % 4 + 1))];
		records.AddRange(Enumerable.Range(0, 40).Select(static i => (DanmakuRecord)DanmakuSamples.Comment(900 + i * 0.01, i % 2 is 0 ? @"好耶" : $@"不同的话{i}", 1000 + i)));
		string path = await WriteDanmakuAsync(directory, records, cancellationToken);
		await File.WriteAllBytesAsync(directory.Combine("20260102_030405.flv"), [1], cancellationToken);

		DanmakuFileDto file = await services.GetRequiredService<IDanmakuAppService>().OpenFileAsync(path, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(file.Source).IsEqualTo(new DanmakuFileSourceDto.Native("测试主播", "测试标题", TimeSpan.Zero));
			await Assert.That(file.IsComplete).IsTrue();
			await Assert.That(file.Entries).Count().IsEqualTo(640);
			await Assert.That(file.Entries[0]).IsEqualTo(new DanmakuFileEntryDto(TimeSpan.Zero, new DanmakuEntryDto.Comment(new DanmuSender(1, "观众", GuardLevel.None, null), "日常0", 0xFFFFFF, false)));
			await Assert.That(file.Density.BinWidth).IsEqualTo(TimeSpan.FromSeconds(5));
			await Assert.That(file.Density.Counts.Sum()).IsEqualTo(640);
			await Assert.That(file.Density.Counts[180]).IsEqualTo(42);
			await Assert.That(file.Highlights).IsEquivalentTo([new DanmakuHighlightDto(TimeSpan.FromSeconds(900), TimeSpan.FromSeconds(900), TimeSpan.FromSeconds(905), "好耶")]);
			await Assert.That(file.VideoPath).IsEqualTo(directory.Combine("20260102_030405.flv"));
			await Assert.That(file.Entries.Select(static entry => entry.Time)).IsInOrder().Because("文件里 900 秒附近的弹幕写在最后，打开时要按时间排好");
		}
	}

	// 录制刚开始、还没有记录的文件只有文件头
	[Test]
	public async Task OwnFileWithOnlyTheHeaderOpensAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		await using ServiceProvider services = CreateServices();
		byte[] content = await DanmakuSamples.WriteAsync([], cancellationToken);
		int headerEnd = content.AsSpan().IndexOf("/>\n"u8) + 3;
		string path = directory.Combine("20260102_030405.xml");
		await File.WriteAllBytesAsync(path, content.AsSpan(0, headerEnd).ToArray(), cancellationToken);

		DanmakuFileDto file = await services.GetRequiredService<IDanmakuAppService>().OpenFileAsync(path, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(file.Source).IsTypeOf<DanmakuFileSourceDto.Native>();
			await Assert.That(file.IsComplete).IsFalse();
			await Assert.That(file.Entries).IsEmpty();
		}
	}

	[Test]
	public async Task DensityBinsWidenInWholeSecondsForLongFilesAsync()
	{
		DanmakuDensityDto density = DanmakuAppService.ComputeDensity([DanmakuSamples.Comment(0), DanmakuSamples.Comment(12 * 3600)]);

		using (Assert.Multiple())
		{
			await Assert.That(density.BinWidth).IsEqualTo(TimeSpan.FromSeconds(22));
			await Assert.That(density.Counts).Count().IsLessThanOrEqualTo(DanmakuAppService.MaxDensityBins);
			await Assert.That((density.Counts[0], density.Counts[^1])).IsEqualTo((1, 1));
		}
	}

	[Test]
	public async Task NonDanmakuFileIsRejectedAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		await using ServiceProvider services = CreateServices();
		string path = directory.Combine("a.xml");
		await File.WriteAllTextAsync(path, "not xml", cancellationToken);

		await Assert.ThrowsAsync<InvalidDataException>(() => services.GetRequiredService<IDanmakuAppService>().OpenFileAsync(path, cancellationToken));
	}

	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task ChangingTheOffsetRewritesTheFileAndRegeneratesTheSubtitleAsync(bool showPaidMessages, CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		AppConfig config = AppConfig.CreateDefault();
		config.ShowPaidMessagesInSubtitle = showPaidMessages;
		await using ServiceProvider services = CreateServices(config: config);
		string path = await WriteDanmakuAsync(directory, [DanmakuSamples.Comment(10), new DanmakuRecord.SuperChat(TimeSpan.FromSeconds(20), 1, "老板", 30000, TimeSpan.FromSeconds(60), "醒目留言")], cancellationToken);
		await File.WriteAllBytesAsync(directory.Combine("20260102_030405.flv"), Flv(1280, 720), cancellationToken);

		IDanmakuAppService danmaku = services.GetRequiredService<IDanmakuAppService>();
		DanmakuOffsetResult result = await danmaku.ChangeOffsetAsync(path, TimeSpan.FromSeconds(2), cancellationToken);
		DanmakuFileDto file = await danmaku.OpenFileAsync(path, cancellationToken);
		string subtitle = await File.ReadAllTextAsync(directory.Combine("20260102_030405.ass"), cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(result).IsEqualTo(new DanmakuOffsetResult.Updated(directory.Combine("20260102_030405.ass")));
			await Assert.That(file.Source).IsEqualTo(new DanmakuFileSourceDto.Native("测试主播", "测试标题", TimeSpan.FromSeconds(2)));
			await Assert.That(file.Entries.Select(static entry => entry.Time)).IsEquivalentTo([TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(22)], CollectionOrdering.Matching);
			await Assert.That(subtitle).Contains("PlayResX: 1280");
			await Assert.That(subtitle.Contains("}醒目留言", StringComparison.Ordinal)).IsEqualTo(showPaidMessages);
		}
	}

	[Test]
	public async Task SubtitleOpenElsewhereIsReportedAfterTheFileIsRewrittenAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		await using ServiceProvider services = CreateServices();
		string path = await WriteDanmakuAsync(directory, [DanmakuSamples.Comment(10)], cancellationToken);
		await File.WriteAllBytesAsync(directory.Combine("20260102_030405_fmp4.mp4"), Fmp4Samples.TimedInit(1000, 1920, 1080), cancellationToken);
		string subtitle = directory.Combine("20260102_030405.ass");
		await File.WriteAllTextAsync(subtitle, "old", cancellationToken);
		IDanmakuAppService danmaku = services.GetRequiredService<IDanmakuAppService>();
		DanmakuOffsetResult result;

		// 播放器独占打开时写不进去
		await using (new FileStream(subtitle, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
		{
			result = await danmaku.ChangeOffsetAsync(path, TimeSpan.FromSeconds(-1), cancellationToken);
		}

		DanmakuFileDto file = await danmaku.OpenFileAsync(path, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(result).IsTypeOf<DanmakuOffsetResult.SubtitleNotWritten>()
				.And.Member(static notWritten => notWritten.Subtitle, written => written.IsEqualTo(subtitle))
				.And.Member(static notWritten => notWritten.Error, static error => error.IsNotEmpty());
			await Assert.That(file.Source).IsEqualTo(new DanmakuFileSourceDto.Native("测试主播", "测试标题", TimeSpan.FromSeconds(-1)));
			await Assert.That(file.Entries.Select(static entry => entry.Time)).IsEquivalentTo([TimeSpan.FromSeconds(9)]);
			await Assert.That(await File.ReadAllTextAsync(subtitle, cancellationToken)).IsEqualTo("old");
		}
	}

	// 缺 moov 的 .mp4 读不到尺寸，改用同主干的源录像
	[Test]
	public async Task SubtitleSizeFallsBackToTheNextVideoWhenTheMp4IsIncompleteAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		await using ServiceProvider services = CreateServices();
		string path = await WriteDanmakuAsync(directory, [DanmakuSamples.Comment(10)], cancellationToken);
		await File.WriteAllBytesAsync(directory.Combine("20260102_030405.mp4"), [0, 0, 0, 16, .. "ftypisom"u8, 0, 0, 0, 0, 0, 0, 0, 0, .. "mdat"u8], cancellationToken);
		await File.WriteAllBytesAsync(directory.Combine("20260102_030405.flv"), Flv(720, 1280), cancellationToken);

		await services.GetRequiredService<IDanmakuAppService>().ChangeOffsetAsync(path, TimeSpan.FromSeconds(1), cancellationToken);
		string subtitle = await File.ReadAllTextAsync(directory.Combine("20260102_030405.ass"), cancellationToken);

		await Assert.That(subtitle).Contains("PlayResX: 720").And.Contains("PlayResY: 1280");
	}

	[Test]
	public async Task OffsetIsClampedAndNoSubtitleWithoutAVideoAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		await using ServiceProvider services = CreateServices();
		string path = await WriteDanmakuAsync(directory, [DanmakuSamples.Comment(100)], cancellationToken);

		IDanmakuAppService danmaku = services.GetRequiredService<IDanmakuAppService>();
		DanmakuOffsetResult result = await danmaku.ChangeOffsetAsync(path, TimeSpan.FromMinutes(10), cancellationToken);
		DanmakuFileDto file = await danmaku.OpenFileAsync(path, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(result).IsEqualTo(new DanmakuOffsetResult.Updated(null));
			await Assert.That(file.Source).IsEqualTo(new DanmakuFileSourceDto.Native("测试主播", "测试标题", TimeSpan.FromSeconds(60)));
			await Assert.That(File.Exists(directory.Combine("20260102_030405.ass"))).IsFalse();
		}
	}

	[Test]
	public async Task FilesOfOtherProgramsCannotBeRewrittenAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		await using ServiceProvider services = CreateServices();
		string path = directory.Combine("a.xml");
		await File.WriteAllTextAsync(path, """<?xml version="1.0"?><i><d p="1,1,25,16777215,0,0,0,0">a</d></i>""", cancellationToken);

		await Assert.ThrowsAsync<InvalidDataException>(() => services.GetRequiredService<IDanmakuAppService>().ChangeOffsetAsync(path, TimeSpan.FromSeconds(1), cancellationToken));
	}

	[Test]
	public async Task HighlightIsCutFromTheVideoWithMarginsAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		CutRecorder cuts = new();
		await using ServiceProvider services = CreateServices(cuts);
		string path = await WriteDanmakuAsync(directory, [DanmakuSamples.Comment(10)], cancellationToken);
		string video = directory.Combine("20260102_030405.mp4");
		await File.WriteAllBytesAsync(video, [1], cancellationToken);
		await File.WriteAllBytesAsync(directory.Combine("20260102_030405.flv"), [1], cancellationToken);

		string destination = await services.GetRequiredService<IDanmakuAppService>().QueueHighlightExportAsync(path, Highlight, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(destination).IsEqualTo(directory.Combine("20260102_030405.highlight-010150.mp4"));
			await Assert.That(cuts.Cuts).IsEquivalentTo([new CutInput(video, destination, TimeSpan.FromSeconds(3710), TimeSpan.FromSeconds(3755))]);
		}
	}

	// 排着队的剪辑还没写出文件，同一段再导出一次也不能用它的输出
	[Test]
	public async Task HighlightAvoidsTheOutputOfQueuedCutsAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string queued = directory.Combine("20260102_030405.highlight-010150.mp4");
		FakeBackgroundTasks tasks = new([new BackgroundTaskSnapshot(Guid.NewGuid(), "cut", null, queued, BackgroundTaskStatus.Queued, null, null, null), new BackgroundTaskSnapshot(Guid.NewGuid(), "cut", null, directory.Combine("20260102_030405.highlight-010150-2.mp4"), BackgroundTaskStatus.Failed, null, null, null)]);
		await using ServiceProvider services = CreateServices(tasks: tasks);
		string path = await WriteDanmakuAsync(directory, [DanmakuSamples.Comment(10)], cancellationToken);
		await File.WriteAllBytesAsync(directory.Combine("20260102_030405.mp4"), [1], cancellationToken);

		string destination = await services.GetRequiredService<IDanmakuAppService>().QueueHighlightExportAsync(path, Highlight, cancellationToken);

		await Assert.That(destination).IsEqualTo(directory.Combine("20260102_030405.highlight-010150-2.mp4"));
	}

	[Test]
	public async Task HighlightOfATitleEndingInFmp4KeepsTheWholeStemAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		await using ServiceProvider services = CreateServices();
		string path = await WriteDanmakuAsync(directory, [DanmakuSamples.Comment(10)], cancellationToken, "20260102_030405_a_fmp4.xml");
		await File.WriteAllBytesAsync(directory.Combine("20260102_030405_a_fmp4.mp4"), [1], cancellationToken);

		string destination = await services.GetRequiredService<IDanmakuAppService>().QueueHighlightExportAsync(path, Highlight, cancellationToken);

		await Assert.That(destination).IsEqualTo(directory.Combine("20260102_030405_a_fmp4.highlight-010150.mp4"));
	}

	[Test]
	public async Task HighlightWithoutAVideoIsRejectedAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		await using ServiceProvider services = CreateServices();
		string path = await WriteDanmakuAsync(directory, [DanmakuSamples.Comment(10)], cancellationToken);

		await Assert.ThrowsAsync<FileNotFoundException>(() => services.GetRequiredService<IDanmakuAppService>().QueueHighlightExportAsync(path, Highlight, cancellationToken));
	}

	[Test]
	[Arguments("a", "a_fmp4.mp4", "a.highlight-000005.mp4")]
	[Arguments("a", "a.flv", "a.highlight-000005.flv")]
	[Arguments("a", "a.ts", "a.highlight-000005.ts")]
	[Arguments("a_fmp4", "a_fmp4.mp4", "a_fmp4.highlight-000005.mp4")]
	public async Task HighlightClipsKeepTheContainerAndDoNotLookLikeRecordingsAsync(string stem, string video, string expected)
	{
		string path = DanmakuAppService.GetHighlightPath(stem, video, TimeSpan.FromSeconds(5), static _ => false);

		using (Assert.Multiple())
		{
			await Assert.That(path).IsEqualTo(expected);
			await Assert.That(RecordingFileLayout.GetVideoPaths(stem)).DoesNotContain(path);
		}
	}

	[Test]
	public async Task ExistingHighlightClipsAreNotOverwrittenAsync()
	{
		HashSet<string> existing = ["a.highlight-000005.mp4", "a.highlight-000005-2.mp4"];

		await Assert.That(DanmakuAppService.GetHighlightPath("a", "a.mp4", TimeSpan.FromSeconds(5), existing.Contains)).IsEqualTo("a.highlight-000005-3.mp4");
	}

	[Test]
	public async Task SubscribingToAnUnknownRoomFailsAsync()
	{
		await using ServiceProvider services = CreateServices();
		IDanmakuAppService danmaku = services.GetRequiredService<IDanmakuAppService>();

		await Assert.That(() => danmaku.Subscribe(1)).Throws<KeyNotFoundException>();
	}

	private sealed class CutRecorder : IMediaToolAppService
	{
		public ConcurrentQueue<CutInput> Cuts { get; } = new();

		public Task<string?> GetFFmpegVersionAsync(CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task<Task<BackgroundTaskSnapshot>> QueueRemuxAsync(RemuxInput input, CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task QueueCutAsync(CutInput input, CancellationToken cancellationToken = default)
		{
			Cuts.Enqueue(input);
			return Task.CompletedTask;
		}
	}

	private sealed class FakeBackgroundTasks(IReadOnlyList<BackgroundTaskSnapshot> tasks) : IBackgroundTaskAppService
	{
		public long Version => 0;

		public bool HasActiveTasks => tasks.Count > 0;

		public IReadOnlyList<BackgroundTaskSnapshot> GetTasks()
		{
			return tasks;
		}

		public void Cancel(Guid id)
		{
			throw new NotSupportedException();
		}

		public void CancelAll()
		{
			throw new NotSupportedException();
		}

		public void RemoveFinished()
		{
			throw new NotSupportedException();
		}
	}
}
