using BilibiliLiveRecordDownLoader.Application.Contracts.Danmaku;
using BilibiliLiveRecordDownLoader.Application.Contracts.Media;
using BilibiliLiveRecordDownLoader.Application.Contracts.Platform;
using BilibiliLiveRecordDownLoader.Application.Danmaku;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Danmaku;

public sealed class DanmakuAppServiceTest
{
	private static ServiceProvider CreateServices(CutRecorder? cuts = null, AppConfig? config = null)
	{
		return new ServiceCollection()
			.AddLogging()
			.AddSingleton<TimeProvider>(new FakeTimeProvider())
			.AddSingleton<IAppConfigManager>(new FakeConfigManager(config))
			.AddSingleton<INotificationService, RecordingNotifications>()
			.AddSingleton<IMediaToolAppService>(cuts ?? new CutRecorder())
			.AddApplication()
			.BuildServiceProvider();
	}

	private static async Task<string> WriteDanmakuAsync(TestDirectory directory, IEnumerable<DanmakuRecord> records, string name = "20260102_030405.xml")
	{
		string path = directory.Combine(name);
		await File.WriteAllBytesAsync(path, await DanmakuSamples.WriteAsync(records));
		return path;
	}

	[Test]
	public async Task OpeningAFileGivesEntriesDensityHighlightsAndTheVideoAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		await using ServiceProvider services = CreateServices();
		List<DanmakuRecord> records = [.. Enumerable.Range(0, 600).Select(static i => (DanmakuRecord)DanmakuSamples.Comment(i * 3, $@"日常{i}", i % 4 + 1))];
		records.AddRange(Enumerable.Range(0, 40).Select(static i => (DanmakuRecord)DanmakuSamples.Comment(900 + i * 0.01, i % 2 is 0 ? @"好耶" : $@"不同的话{i}", 1000 + i)));
		string path = await WriteDanmakuAsync(directory, records);
		await File.WriteAllBytesAsync(directory.Combine("20260102_030405.flv"), [1], cancellationToken);

		DanmakuFileDto file = await services.GetRequiredService<IDanmakuAppService>().OpenFileAsync(path, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(file.Source is DanmakuFileSourceDto.Native { Header: { Title: "测试标题", RoomId: 21452505 } }).IsTrue();
			await Assert.That(file.IsComplete).IsTrue();
			await Assert.That(file.Entries.Count).IsEqualTo(640);
			await Assert.That(file.Entries[0]).IsEqualTo(new DanmakuFileEntryDto(TimeSpan.Zero, new DanmakuEntryDto.Comment(new BilibiliLive.Danmu.DanmuSender(1, "观众", BilibiliLive.Danmu.GuardLevel.None, null), "日常0", 0xFFFFFF, false)));
			await Assert.That(file.Density.BinWidth).IsEqualTo(TimeSpan.FromSeconds(5));
			await Assert.That(file.Density.Counts.Sum()).IsEqualTo(640);
			await Assert.That(file.Density.Counts[180]).IsEqualTo(42);
			await Assert.That(file.Highlights.Single()).IsEqualTo(new DanmakuHighlightDto(TimeSpan.FromSeconds(900), TimeSpan.FromSeconds(900), TimeSpan.FromSeconds(905), 42, "好耶"));
			await Assert.That(file.VideoPath).IsEqualTo(directory.Combine("20260102_030405.flv"));
		}
	}

	[Test]
	public async Task DensityBinsWidenInWholeSecondsForLongFilesAsync()
	{
		DanmakuDensityDto density = DanmakuAppService.ComputeDensity([DanmakuSamples.Comment(0), DanmakuSamples.Comment(12 * 3600)]);

		using (Assert.Multiple())
		{
			await Assert.That(density.BinWidth).IsEqualTo(TimeSpan.FromSeconds(22));
			await Assert.That(density.Counts.Count).IsLessThanOrEqualTo(DanmakuAppService.MaxDensityBins);
			await Assert.That(density.Counts[0] + density.Counts[^1]).IsEqualTo(2);
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
	public async Task RegeneratedSubtitlesFollowThePaidMessageSettingAsync(bool show, CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		AppConfig config = AppConfig.CreateDefault();
		config.ShowPaidMessagesInSubtitle = show;
		await using ServiceProvider services = CreateServices(config: config);
		string path = await WriteDanmakuAsync(directory, [DanmakuSamples.Comment(10), new DanmakuRecord.SuperChat(TimeSpan.FromSeconds(20), 1, "老板", 30000, TimeSpan.FromSeconds(60), "醒目留言")]);
		await File.WriteAllBytesAsync(directory.Combine("20260102_030405.flv"), [.. FlvSamples.Header, .. FlvSamples.MetadataWithSize(1280, 720)], cancellationToken);

		await services.GetRequiredService<IDanmakuAppService>().ChangeOffsetAsync(path, TimeSpan.FromSeconds(2), cancellationToken);
		string subtitle = await File.ReadAllTextAsync(directory.Combine("20260102_030405.ass"), cancellationToken);

		await Assert.That(subtitle.Contains("}醒目留言", StringComparison.Ordinal)).IsEqualTo(show);
	}

	[Test]
	public async Task ChangingTheOffsetRewritesTheFileAndRegeneratesTheSubtitleAtTheVideoSizeAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		await using ServiceProvider services = CreateServices();
		string path = await WriteDanmakuAsync(directory, [DanmakuSamples.Comment(10)]);
		await File.WriteAllBytesAsync(directory.Combine("20260102_030405.flv"), [.. FlvSamples.Header, .. FlvSamples.MetadataWithSize(1280, 720)], cancellationToken);

		DanmakuOffsetResult result = await services.GetRequiredService<IDanmakuAppService>().ChangeOffsetAsync(path, TimeSpan.FromSeconds(2), cancellationToken);
		DanmakuFile file = await DanmakuXmlReader.ReadFileAsync(path, cancellationToken);
		string subtitle = await File.ReadAllTextAsync(directory.Combine("20260102_030405.ass"), cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(result is DanmakuOffsetResult.Updated { Header.Offset.TotalSeconds: 2 } updated && updated.Subtitle == directory.Combine("20260102_030405.ass")).IsTrue();
			await Assert.That(file.Records.Single().Time).IsEqualTo(TimeSpan.FromSeconds(12));
			await Assert.That(subtitle).Contains("PlayResX: 1280");
		}
	}

	[Test]
	public async Task SubtitleOpenElsewhereIsReportedAfterTheFileIsRewrittenAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		await using ServiceProvider services = CreateServices();
		string path = await WriteDanmakuAsync(directory, [DanmakuSamples.Comment(10)]);
		await File.WriteAllBytesAsync(directory.Combine("20260102_030405_fmp4.mp4"), Fmp4Samples.TimedInit(1000, 1920, 1080), cancellationToken);
		string subtitle = directory.Combine("20260102_030405.ass");
		await File.WriteAllTextAsync(subtitle, "old", cancellationToken);
		DanmakuOffsetResult result;

		// 播放器独占打开时写不进去
		await using (new FileStream(subtitle, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
		{
			result = await services.GetRequiredService<IDanmakuAppService>().ChangeOffsetAsync(path, TimeSpan.FromSeconds(-1), cancellationToken);
		}

		using (Assert.Multiple())
		{
			await Assert.That(result is DanmakuOffsetResult.SubtitleInUse { Header.Offset.TotalSeconds: -1 } inUse && inUse.Subtitle == subtitle).IsTrue();
			await Assert.That((await DanmakuXmlReader.ReadFileAsync(path, cancellationToken)).Records.Single().Time).IsEqualTo(TimeSpan.FromSeconds(9));
			await Assert.That(await File.ReadAllTextAsync(subtitle, cancellationToken)).IsEqualTo("old");
		}
	}

	[Test]
	public async Task OffsetIsClampedAndNoSubtitleWithoutAVideoAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		await using ServiceProvider services = CreateServices();
		string path = await WriteDanmakuAsync(directory, [DanmakuSamples.Comment(100)]);

		DanmakuOffsetResult result = await services.GetRequiredService<IDanmakuAppService>().ChangeOffsetAsync(path, TimeSpan.FromMinutes(10), cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(result).IsEqualTo(new DanmakuOffsetResult.Updated(result.Header, null));
			await Assert.That(result.Header.Offset).IsEqualTo(TimeSpan.FromSeconds(60));
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
		string path = await WriteDanmakuAsync(directory, [DanmakuSamples.Comment(10)]);
		string video = directory.Combine("20260102_030405.mp4");
		await File.WriteAllBytesAsync(video, [1], cancellationToken);
		await File.WriteAllBytesAsync(directory.Combine("20260102_030405.flv"), [1], cancellationToken);

		string destination = await services.GetRequiredService<IDanmakuAppService>().QueueHighlightExportAsync(path, new DanmakuHighlightDto(TimeSpan.FromSeconds(3725), TimeSpan.FromSeconds(3730), TimeSpan.FromSeconds(3740), 50, "好耶"), cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(destination).IsEqualTo(directory.Combine("20260102_030405.highlight-010150.mp4"));
			await Assert.That(cuts.Cuts.Single()).IsEqualTo(new CutInput(video, destination, TimeSpan.FromSeconds(3710), TimeSpan.FromSeconds(3755)));
		}
	}

	[Test]
	public async Task HighlightWithoutAVideoIsRejectedAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		await using ServiceProvider services = CreateServices();
		string path = await WriteDanmakuAsync(directory, [DanmakuSamples.Comment(10)]);

		await Assert.ThrowsAsync<FileNotFoundException>(() => services.GetRequiredService<IDanmakuAppService>().QueueHighlightExportAsync(path, new DanmakuHighlightDto(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromSeconds(5), 10, string.Empty), cancellationToken));
	}

	[Test]
	[Arguments("a_fmp4.mp4", "a.highlight-000005.mp4")]
	[Arguments("a.flv", "a.highlight-000005.flv")]
	[Arguments("a.ts", "a.highlight-000005.ts")]
	public async Task HighlightClipsKeepTheContainerAndDoNotLookLikeRecordingsAsync(string video, string expected)
	{
		string path = DanmakuAppService.GetHighlightPath(video, TimeSpan.FromSeconds(5), static _ => false);

		using (Assert.Multiple())
		{
			await Assert.That(path).IsEqualTo(expected);
			await Assert.That(RecordingFileLayout.GetVideoPaths("a")).DoesNotContain(path);
		}
	}

	[Test]
	public async Task ExistingHighlightClipsAreNotOverwrittenAsync()
	{
		HashSet<string> existing = ["a.highlight-000005.mp4", "a.highlight-000005-2.mp4"];

		await Assert.That(DanmakuAppService.GetHighlightPath("a.mp4", TimeSpan.FromSeconds(5), existing.Contains)).IsEqualTo("a.highlight-000005-3.mp4");
	}

	[Test]
	public async Task SubscribingToAnUnknownRoomFailsAsync()
	{
		await using ServiceProvider services = CreateServices();

		IDanmakuAppService danmaku = services.GetRequiredService<IDanmakuAppService>();

		Assert.Throws<KeyNotFoundException>(() => danmaku.Subscribe(1));
		await Task.CompletedTask;
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
}
