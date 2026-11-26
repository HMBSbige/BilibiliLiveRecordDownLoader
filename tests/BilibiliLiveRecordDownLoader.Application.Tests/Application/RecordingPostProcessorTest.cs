using BilibiliLive.Danmu;
using BilibiliLiveRecordDownLoader.Application.Contracts.Media;
using BilibiliLiveRecordDownLoader.Application.Danmaku;
using BilibiliLiveRecordDownLoader.Application.Recording;
using BilibiliLiveRecordDownLoader.Application.Tests.Danmaku;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using BilibiliLiveRecordDownLoader.Domain.Shared.Localization;
using OS = TUnit.Core.Enums.OS;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class RecordingPostProcessorTest
{
	private const string Streamer = "streamer";
	private const string Stem = "20260102_030405";

	private static readonly DanmakuProfile NoDanmaku = DanmakuProfile.Create(AppConfig.CreateDefault(), new RoomConfig { RoomId = 1 });

	[Test]
	public async Task TooSmallRecordingIsDeletedWithItsDanmakuAndSubtitleInsteadOfConvertedAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string path = CreateRecording(directory, RecordingPostProcessor.MinimumLength - 1);
		string danmaku = directory.Combine(Stem + ".xml");
		string subtitle = directory.Combine(Stem + ".ass");
		string rawMessages = directory.Combine(Stem + ".danmaku.jsonl");
		await File.WriteAllTextAsync(danmaku, "x", cancellationToken);
		await File.WriteAllTextAsync(subtitle, "x", cancellationToken);
		await File.WriteAllTextAsync(rawMessages, "x", cancellationToken);
		RecordingMediaTools mediaTools = new();
		RecordingNotifications notifications = new();

		await Create(mediaTools, notifications).ProcessAsync(path, CreateProfile(directory), Streamer, new DanmakuSegment(danmaku, null, null), CreateDanmakuProfile());

		using (Assert.Multiple())
		{
			await Assert.That((File.Exists(path), File.Exists(danmaku), File.Exists(subtitle))).IsEqualTo((false, false, false));
			await Assert.That(File.Exists(rawMessages)).IsTrue();
			await Assert.That(mediaTools.Remuxes).IsEmpty();
			await Assert.That(notifications.Shown).IsEmpty();
		}
	}

	[Test]
	[RunOn(OS.Windows)]
	public async Task TooSmallRecordingThatCannotBeDeletedKeepsItsDanmakuAndSubtitleAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string path = CreateRecording(directory, RecordingPostProcessor.MinimumLength - 1);
		string danmaku = directory.Combine(Stem + ".xml");
		string subtitle = directory.Combine(Stem + ".ass");
		await File.WriteAllTextAsync(danmaku, "x", cancellationToken);
		await File.WriteAllTextAsync(subtitle, "x", cancellationToken);

		await using (new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read))
		{
			await Create(new RecordingMediaTools()).ProcessAsync(path, CreateProfile(directory), Streamer, new DanmakuSegment(danmaku, null, null), CreateDanmakuProfile());
		}

		await Assert.That((File.Exists(path), File.Exists(danmaku), File.Exists(subtitle))).IsEqualTo((true, true, true));
	}

	[Test]
	public async Task FailedConversionIsNotifiedWithTheFileNameAsync()
	{
		using TestDirectory directory = new();
		string path = CreateRecording(directory, RecordingPostProcessor.MinimumLength);
		RecordingNotifications notifications = new();

		await Create(new QueuedRemuxes(BackgroundTaskStatus.Failed, "Invalid data found"), notifications).ProcessAsync(path, CreateProfile(directory), Streamer, null, NoDanmaku);

		ShownNotification shown = await Assert.That(notifications.Shown).HasSingleItem();

		using (Assert.Multiple())
		{
			await Assert.That(shown.Title).Contains(Streamer);
			await Assert.That(shown.Message).Contains(Path.GetFileName(path)).And.Contains("Invalid data found");
			await Assert.That(shown.Type).IsEqualTo(AppNotificationType.Error);
		}
	}

	[Test]
	public async Task ConversionThatCannotBeQueuedIsNotifiedAsync()
	{
		using TestDirectory directory = new();
		string path = CreateRecording(directory, RecordingPostProcessor.MinimumLength);
		RecordingNotifications notifications = new();

		await Create(new QueuedRemuxes(new FileNotFoundException("FFmpeg is missing")), notifications).ProcessAsync(path, CreateProfile(directory), Streamer, null, NoDanmaku);

		ShownNotification shown = await Assert.That(notifications.Shown).HasSingleItem();
		await Assert.That(shown.Message).Contains("FFmpeg is missing");
	}

	[Test]
	[Arguments(BackgroundTaskStatus.Canceled, true)]
	[Arguments(BackgroundTaskStatus.Failed, false)]
	public async Task ConversionIsNotNotifiedWhenCanceledOrTurnedOffAsync(BackgroundTaskStatus status, bool notifyOnRecordingError)
	{
		using TestDirectory directory = new();
		string path = CreateRecording(directory, RecordingPostProcessor.MinimumLength);
		RecordingNotifications notifications = new();
		AppConfig config = AppConfig.CreateDefault();
		config.NotifyOnRecordingError = notifyOnRecordingError;

		await Create(new QueuedRemuxes(status, "boom"), notifications, config).ProcessAsync(path, CreateProfile(directory), Streamer, null, NoDanmaku);

		await Assert.That(notifications.Shown).IsEmpty();
	}

	[Test]
	public async Task SubtitleIsGeneratedAtTheVideoSizeAndChaptersGoIntoTheConversionAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string path = CreateRecording(directory, RecordingPostProcessor.MinimumLength);
		string danmaku = await WriteDanmakuAsync(directory, [DanmakuSamples.Comment(10), new DanmakuRecord.TitleChange(TimeSpan.FromSeconds(100), "新标题")], cancellationToken);
		RecordingMediaTools mediaTools = new();

		await Create(mediaTools).ProcessAsync(path, CreateProfile(directory), Streamer, new DanmakuSegment(danmaku, new VideoResolution(1280, 720), TimeSpan.FromSeconds(200)), CreateDanmakuProfile(writeChapters: true));

		RemuxInput remux = await Assert.That(mediaTools.Remuxes).HasSingleItem();
		RemuxMode.WithChapters? withChapters = await Assert.That(remux.Mode).IsTypeOf<RemuxMode.WithChapters>();

		using (Assert.Multiple())
		{
			await Assert.That(await File.ReadAllTextAsync(directory.Combine(Stem + ".ass"), cancellationToken)).Contains("PlayResY: 720");
			await Assert.That(mediaTools.Chapters[remux.Destination]).Contains("START=0\nEND=100000\ntitle=测试标题\n").And.Contains($"START=100000\nEND=200000\ntitle={AppLanguage.Current.ChapterTitleChange.ToString("新标题")}\n");
			await Assert.That(File.Exists(withChapters!.Path)).IsFalse();
		}
	}

	[Test]
	public async Task NothingExtraIsWrittenWhenTurnedOffAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string path = CreateRecording(directory, RecordingPostProcessor.MinimumLength);
		string danmaku = await WriteDanmakuAsync(directory, [DanmakuSamples.Comment(10)], cancellationToken);
		RecordingMediaTools mediaTools = new();

		await Create(mediaTools).ProcessAsync(path, CreateProfile(directory), Streamer, new DanmakuSegment(danmaku, null, null), CreateDanmakuProfile(generateSubtitle: false));

		RemuxInput remux = await Assert.That(mediaTools.Remuxes).HasSingleItem();

		using (Assert.Multiple())
		{
			await Assert.That(File.Exists(directory.Combine(Stem + ".ass"))).IsFalse();
			await Assert.That(remux.Mode).IsEqualTo(new RemuxMode.Plain());
		}
	}

	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task PaidMessagesInTheSubtitleFollowTheSettingAsync(bool show, CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string path = CreateRecording(directory, RecordingPostProcessor.MinimumLength);
		string danmaku = await WriteDanmakuAsync(directory, [DanmakuSamples.Comment(10), new DanmakuRecord.Guard(TimeSpan.FromSeconds(20), 1, "舰长", GuardLevel.Captain, 3, 594000)], cancellationToken);

		await Create(new RecordingMediaTools()).ProcessAsync(path, CreateProfile(directory), Streamer, new DanmakuSegment(danmaku, null, null), CreateDanmakuProfile(showPaidMessages: show));

		string subtitle = await File.ReadAllTextAsync(directory.Combine(Stem + ".ass"), cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(subtitle.Contains(",Message,,", StringComparison.Ordinal)).IsEqualTo(show);
			await Assert.That(subtitle.Contains(DanmakuTexts.SubtitleLabels.Guard("舰长", GuardLevel.Captain, 3, false), StringComparison.Ordinal)).IsEqualTo(show);
		}
	}

	[Test]
	public async Task ChaptersNeedTheConversionAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string path = CreateRecording(directory, RecordingPostProcessor.MinimumLength);
		string danmaku = await WriteDanmakuAsync(directory, [DanmakuSamples.Comment(10)], cancellationToken);
		RecordingMediaTools mediaTools = new();
		bool wroteChaptersFile = false;
		RecordingPostProcessor processor = new(mediaTools, new FakeConfigManager(), new RecordingNotifications(), NullLogger<RecordingPostProcessor>.Instance)
		{
			WriteChaptersFile = (_, _, _) =>
			{
				wroteChaptersFile = true;
				return Task.CompletedTask;
			}
		};

		await processor.ProcessAsync(path, CreateProfile(directory, convertToMp4: false), Streamer, new DanmakuSegment(danmaku, null, null), CreateDanmakuProfile(writeChapters: true));

		using (Assert.Multiple())
		{
			await Assert.That(mediaTools.Remuxes).IsEmpty();
			await Assert.That(File.Exists(directory.Combine(Stem + ".ass"))).IsTrue();
			await Assert.That(wroteChaptersFile).IsFalse();
		}
	}

	[Test]
	public async Task ChaptersThatCannotBeWrittenAreLeftOutOfTheConversionAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string path = CreateRecording(directory, RecordingPostProcessor.MinimumLength);
		string danmaku = await WriteDanmakuAsync(directory, [DanmakuSamples.Comment(10)], cancellationToken);
		RecordingMediaTools mediaTools = new();
		RecordingNotifications notifications = new();
		string? chaptersFile = null;
		RecordingPostProcessor processor = new(mediaTools, new FakeConfigManager(), notifications, NullLogger<RecordingPostProcessor>.Instance)
		{
			WriteChaptersFile = async (file, _, ct) =>
			{
				chaptersFile = file;
				await File.WriteAllTextAsync(file, ";FFMETADATA1\n", ct);
				throw new IOException("disk full");
			}
		};

		await processor.ProcessAsync(path, CreateProfile(directory), Streamer, new DanmakuSegment(danmaku, null, TimeSpan.FromSeconds(60)), CreateDanmakuProfile(writeChapters: true));

		RemuxInput remux = await Assert.That(mediaTools.Remuxes).HasSingleItem();

		using (Assert.Multiple())
		{
			await Assert.That(remux.Mode).IsEqualTo(new RemuxMode.Plain());
			await Assert.That(File.Exists(directory.Combine(Stem + ".ass"))).IsTrue();
			await Assert.That(chaptersFile).IsNotNull();
			await Assert.That(File.Exists(chaptersFile)).IsFalse();
			await Assert.That(notifications.Shown).IsEmpty();
		}
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task Fmp4RecordingIsConvertedInPlaceAndRenamedWithoutFFmpegAsync(bool convertToMp4, CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string path = WriteFmp4(directory, HybridSamples.Recording());
		RecordingMediaTools mediaTools = new();
		RecordingNotifications notifications = new();

		await Create(mediaTools, notifications).ProcessAsync(path, CreateProfile(directory, convertToMp4), Streamer, null, NoDanmaku);

		string destination = directory.Combine(Stem + ".mp4");

		using (Assert.Multiple())
		{
			await Assert.That((File.Exists(path), File.Exists(destination))).IsEqualTo((false, true));
			await Assert.That(await ReadTopLevelTypesAsync(destination, cancellationToken)).IsEquivalentTo(["ftyp", "mdat", "moov"], CollectionOrdering.Matching);
			await Assert.That(mediaTools.Remuxes).IsEmpty();
			await Assert.That(notifications.Shown).IsEmpty();
		}
	}

	// 转换后的文件已经能播放，只是名字还带着 _fmp4
	[Test]
	public async Task Fmp4RecordingKeepsItsNameWhenTheMp4NameIsTakenAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string path = WriteFmp4(directory, HybridSamples.Recording());
		string destination = directory.Combine(Stem + ".mp4");
		await File.WriteAllTextAsync(destination, "existing", cancellationToken);
		RecordingNotifications notifications = new();

		await Create(new RecordingMediaTools(), notifications).ProcessAsync(path, CreateProfile(directory), Streamer, null, NoDanmaku);

		using (Assert.Multiple())
		{
			await Assert.That(await File.ReadAllTextAsync(destination, cancellationToken)).IsEqualTo("existing");
			await Assert.That(await ReadTopLevelTypesAsync(path, cancellationToken)).IsEquivalentTo(["ftyp", "mdat", "moov"], CollectionOrdering.Matching);
			await Assert.That(notifications.Shown).IsEmpty();
		}
	}

	[Test]
	public async Task Fmp4RecordingThatCannotBeConvertedIsNotifiedAndLeftAsRecordedAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		byte[] recording = HybridSamples.Recording();
		int movieStart = HybridSamples.FileType().Length;
		int initLength = HybridSamples.Init().Length;
		byte[] movie = Fmp4Samples.Box("moov", [.. recording.AsSpan(movieStart + 8, initLength - movieStart - 8), .. HybridSamples.FullBox("pssh", 0, 0, new byte[20])]);
		byte[] original = [.. HybridSamples.FileType(), .. movie, .. recording.AsSpan(initLength)];
		string path = WriteFmp4(directory, original);
		RecordingNotifications notifications = new();

		await Create(new RecordingMediaTools(), notifications).ProcessAsync(path, CreateProfile(directory), Streamer, null, NoDanmaku);

		ShownNotification shown = await Assert.That(notifications.Shown).HasSingleItem();

		using (Assert.Multiple())
		{
			await Assert.That(shown.Message).Contains(Path.GetFileName(path)).And.Contains(AppLanguage.Current.InPlaceConversionRejected.ToString("encrypted content"));
			await Assert.That(await File.ReadAllBytesAsync(path, cancellationToken)).IsEquivalentTo(original, CollectionOrdering.Matching);
			await Assert.That(File.Exists(directory.Combine(Stem + ".mp4"))).IsFalse();
		}
	}

	[Test]
	[RunOn(OS.Windows)]
	public async Task Fmp4RecordingOpenElsewhereIsNotifiedAndLeftAsRecordedAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		byte[] original = HybridSamples.Recording();
		string path = WriteFmp4(directory, original);
		RecordingNotifications notifications = new();

		await using (new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read))
		{
			await Create(new RecordingMediaTools(), notifications).ProcessAsync(path, CreateProfile(directory), Streamer, null, NoDanmaku);
		}

		ShownNotification shown = await Assert.That(notifications.Shown).HasSingleItem();

		using (Assert.Multiple())
		{
			await Assert.That(shown.Message).Contains(Path.GetFileName(path));
			await Assert.That(await File.ReadAllBytesAsync(path, cancellationToken)).IsEquivalentTo(original, CollectionOrdering.Matching);
			await Assert.That(File.Exists(directory.Combine(Stem + ".mp4"))).IsFalse();
		}
	}

	// fMP4 的章节直接写进转换后的文件，不生成章节文件，也不看转封装设置
	[Test]
	public async Task Fmp4RecordingGetsChaptersWithoutTheConversionSettingAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string path = WriteFmp4(directory, HybridSamples.Recording());
		string danmaku = await WriteDanmakuAsync(directory, [DanmakuSamples.Comment(10), new DanmakuRecord.TitleChange(TimeSpan.FromSeconds(100), "新标题")], cancellationToken);
		bool wroteChaptersFile = false;
		RecordingPostProcessor processor = new(new RecordingMediaTools(), new FakeConfigManager(), new RecordingNotifications(), NullLogger<RecordingPostProcessor>.Instance)
		{
			WriteChaptersFile = (_, _, _) =>
			{
				wroteChaptersFile = true;
				return Task.CompletedTask;
			}
		};

		await processor.ProcessAsync(path, CreateProfile(directory, convertToMp4: false), Streamer, new DanmakuSegment(danmaku, new VideoResolution(1280, 720), TimeSpan.FromSeconds(200)), CreateDanmakuProfile(writeChapters: true));

		byte[] movie = Mp4Inspector.ReadMovie(await File.ReadAllBytesAsync(directory.Combine(Stem + ".mp4"), cancellationToken));

		using (Assert.Multiple())
		{
			await Assert.That(Mp4Inspector.ReadTracksFromMovie(movie).Single(static track => track.Handler is "text").Samples).Count().IsEqualTo(2);
			await Assert.That(Mp4Inspector.Find(movie, "udta", "chpl")?[8]).IsEqualTo((byte)2);
			await Assert.That(wroteChaptersFile).IsFalse();
			await Assert.That(File.Exists(directory.Combine(Stem + ".ass"))).IsTrue();
		}
	}

	private static async Task<IEnumerable<string>> ReadTopLevelTypesAsync(string path, CancellationToken cancellationToken)
	{
		return Mp4Inspector.ReadTopLevel(await File.ReadAllBytesAsync(path, cancellationToken)).Select(static box => box.Type);
	}

	private static RecordingPostProcessor Create(IMediaToolAppService mediaTools, RecordingNotifications? notifications = null, AppConfig? config = null)
	{
		return new RecordingPostProcessor(mediaTools, new FakeConfigManager(config), notifications ?? new RecordingNotifications(), NullLogger<RecordingPostProcessor>.Instance);
	}

	private static DanmakuProfile CreateDanmakuProfile(bool generateSubtitle = true, bool writeChapters = false, bool showPaidMessages = true)
	{
		return new DanmakuProfile(true, true, DanmakuGiftMode.None, true, TimeSpan.Zero, generateSubtitle, showPaidMessages, false, writeChapters);
	}

	private static async Task<string> WriteDanmakuAsync(TestDirectory directory, IEnumerable<DanmakuRecord> records, CancellationToken cancellationToken)
	{
		string path = directory.Combine(Stem + ".xml");
		await File.WriteAllBytesAsync(path, await DanmakuSamples.WriteAsync(records, cancellationToken), cancellationToken);
		return path;
	}

	private static string CreateRecording(TestDirectory directory, long length)
	{
		string path = directory.Combine(Stem + ".flv");
		using FileStream file = File.Create(path);
		file.SetLength(length);
		return path;
	}

	private static string WriteFmp4(TestDirectory directory, byte[] content)
	{
		string path = directory.Combine(Stem + "_fmp4.mp4");
		File.WriteAllBytes(path, content);
		return path;
	}

	private static RecordingProfile CreateProfile(TestDirectory directory, bool convertToMp4 = true)
	{
		AppConfig config = AppConfig.CreateDefault();
		config.RecordDirectory = directory.Root;
		config.ConvertToMp4 = convertToMp4;
		return RecordingProfile.Create(config, new RoomConfig { RoomId = 1 });
	}

	private sealed class QueuedRemuxes : MediaToolAppServiceStub
	{
		private readonly Func<RemuxInput, Task<Task<BackgroundTaskSnapshot>>> _queue;

		public QueuedRemuxes(BackgroundTaskStatus status, string? message = null)
		{
			_queue = input => Task.FromResult(Task.FromResult(new BackgroundTaskSnapshot(Guid.NewGuid(), "remux", input.Source, input.Destination, status, null, null, message)));
		}

		public QueuedRemuxes(Exception exception)
		{
			_queue = _ => Task.FromException<Task<BackgroundTaskSnapshot>>(exception);
		}

		public override Task<Task<BackgroundTaskSnapshot>> QueueRemuxAsync(RemuxInput input, CancellationToken cancellationToken = default)
		{
			return _queue(input);
		}
	}
}
