using BilibiliLive.Danmu;
using BilibiliLiveRecordDownLoader.Application.Contracts.Media;
using BilibiliLiveRecordDownLoader.Application.Danmaku;
using BilibiliLiveRecordDownLoader.Application.Recording;
using BilibiliLiveRecordDownLoader.Application.Tests.Danmaku;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using BilibiliLiveRecordDownLoader.Domain.Shared.Localization;
using System.Collections.Concurrent;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class RecordingPostProcessorTest
{
	private const string Streamer = "streamer";
	private const string Stem = "20260102_030405";

	private static readonly DanmakuProfile NoDanmaku = DanmakuProfile.Create(AppConfig.CreateDefault(), new RoomConfig { RoomId = 1 });

	[Test]
	public async Task RecordingWithoutUsableContentIsDeletedInsteadOfConvertedAsync()
	{
		using TestDirectory directory = new();
		string path = CreateRecording(directory, RecordingPostProcessor.MinimumLength - 1);
		QueuedRemuxes mediaTools = new(BackgroundTaskStatus.Completed);
		RecordingNotifications notifications = new();

		await Create(mediaTools, notifications).ProcessAsync(path, CreateProfile(directory), Streamer, null, NoDanmaku);

		using (Assert.Multiple())
		{
			await Assert.That(File.Exists(path)).IsFalse();
			await Assert.That(mediaTools.Inputs).IsEmpty();
			await Assert.That(notifications.Shown).IsEmpty();
		}
	}

	[Test]
	public async Task TooSmallRecordingTakesItsDanmakuAndSubtitleAlongAsync()
	{
		using TestDirectory directory = new();
		string path = CreateRecording(directory, RecordingPostProcessor.MinimumLength - 1);
		await File.WriteAllTextAsync(directory.Combine(Stem + ".xml"), "x");
		await File.WriteAllTextAsync(directory.Combine(Stem + ".ass"), "x");
		await File.WriteAllTextAsync(directory.Combine(Stem + ".danmaku.jsonl"), "x");

		await Create(new RecordingMediaTools()).ProcessAsync(path, CreateProfile(directory), Streamer, new DanmakuSegment(directory.Combine(Stem + ".xml"), null, null), CreateDanmakuProfile());

		using (Assert.Multiple())
		{
			await Assert.That(File.Exists(path)).IsFalse();
			await Assert.That(File.Exists(directory.Combine(Stem + ".xml"))).IsFalse();
			await Assert.That(File.Exists(directory.Combine(Stem + ".ass"))).IsFalse();
			// 原始消息属于整场直播
			await Assert.That(File.Exists(directory.Combine(Stem + ".danmaku.jsonl"))).IsTrue();
		}
	}

	// 录像被别的程序开着时 Windows 上删不掉它，留下的录像不能没了弹幕和字幕
	[Test]
	public async Task TooSmallRecordingThatCannotBeDeletedKeepsItsDanmakuAndSubtitleAsync()
	{
		using TestDirectory directory = new();
		string path = CreateRecording(directory, RecordingPostProcessor.MinimumLength - 1);
		string danmaku = directory.Combine(Stem + ".xml");
		string subtitle = directory.Combine(Stem + ".ass");
		await File.WriteAllTextAsync(danmaku, "x");
		await File.WriteAllTextAsync(subtitle, "x");

		await using (new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read))
		{
			await Create(new RecordingMediaTools()).ProcessAsync(path, CreateProfile(directory), Streamer, new DanmakuSegment(danmaku, null, null), CreateDanmakuProfile());
		}

		bool isRecordingKept = File.Exists(path);
		await Assert.That((File.Exists(danmaku), File.Exists(subtitle))).IsEqualTo((isRecordingKept, isRecordingKept));
	}

	[Test]
	public async Task RecordingWithContentIsQueuedForConversionAsync()
	{
		using TestDirectory directory = new();
		string path = CreateRecording(directory, RecordingPostProcessor.MinimumLength);
		QueuedRemuxes mediaTools = new(BackgroundTaskStatus.Completed);
		RecordingNotifications notifications = new();

		await Create(mediaTools, notifications).ProcessAsync(path, CreateProfile(directory), Streamer, null, NoDanmaku);

		using (Assert.Multiple())
		{
			await Assert.That(File.Exists(path)).IsTrue();
			await Assert.That(mediaTools.Inputs.Select(static input => input.Source)).IsEquivalentTo([path]);
			await Assert.That(notifications.Shown).IsEmpty();
		}
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
	public async Task CanceledConversionIsNotNotifiedAsync()
	{
		using TestDirectory directory = new();
		string path = CreateRecording(directory, RecordingPostProcessor.MinimumLength);
		RecordingNotifications notifications = new();

		await Create(new QueuedRemuxes(BackgroundTaskStatus.Canceled), notifications).ProcessAsync(path, CreateProfile(directory), Streamer, null, NoDanmaku);

		await Assert.That(notifications.Shown).IsEmpty();
	}

	[Test]
	public async Task FailedConversionIsNotNotifiedWhenRecordingErrorNotificationsAreOffAsync()
	{
		using TestDirectory directory = new();
		string path = CreateRecording(directory, RecordingPostProcessor.MinimumLength);
		RecordingNotifications notifications = new();
		AppConfig config = AppConfig.CreateDefault();
		config.NotifyOnRecordingError = false;

		await Create(new QueuedRemuxes(BackgroundTaskStatus.Failed, "boom"), notifications, config).ProcessAsync(path, CreateProfile(directory), Streamer, null, NoDanmaku);

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
			await Assert.That(await File.ReadAllTextAsync(directory.Combine(Stem + ".ass"))).Contains("PlayResY: 720");
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

		string subtitle = await File.ReadAllTextAsync(directory.Combine(Stem + ".ass"));

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
		AppConfig config = AppConfig.CreateDefault();
		config.RecordDirectory = directory.Root;

		await Create(mediaTools).ProcessAsync(path, RecordingProfile.Create(config, new RoomConfig { RoomId = 1 }), Streamer, new DanmakuSegment(danmaku, null, null), CreateDanmakuProfile(writeChapters: true));

		using (Assert.Multiple())
		{
			await Assert.That(mediaTools.Remuxes).IsEmpty();
			await Assert.That(File.Exists(directory.Combine(Stem + ".ass"))).IsTrue();
			await Assert.That(Directory.GetFiles(directory.Root, "*.ffmetadata")).IsEmpty();
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
		RecordingPostProcessor processor = new(mediaTools, new FakeConfigManager(), notifications, NullLogger<RecordingPostProcessor>.Instance)
		{
			WriteChaptersFile = static async (file, _, cancellationToken) =>
			{
				await File.WriteAllTextAsync(file, ";FFMETADATA1\n", cancellationToken);
				throw new IOException("disk full");
			}
		};

		await processor.ProcessAsync(path, CreateProfile(directory), Streamer, new DanmakuSegment(danmaku, null, TimeSpan.FromSeconds(60)), CreateDanmakuProfile(writeChapters: true));

		RemuxInput remux = await Assert.That(mediaTools.Remuxes).HasSingleItem();

		using (Assert.Multiple())
		{
			await Assert.That(remux.Mode).IsEqualTo(new RemuxMode.Plain());
			await Assert.That(File.Exists(directory.Combine(Stem + ".ass"))).IsTrue();
			await Assert.That(Directory.GetFiles(directory.Root, "*.ffmetadata")).IsEmpty();
			await Assert.That(notifications.Shown).IsEmpty();
		}
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

	private static RecordingProfile CreateProfile(TestDirectory directory)
	{
		AppConfig config = AppConfig.CreateDefault();
		config.RecordDirectory = directory.Root;
		config.ConvertToMp4 = true;
		return RecordingProfile.Create(config, new RoomConfig { RoomId = 1 });
	}

	private sealed class QueuedRemuxes : IMediaToolAppService
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

		public ConcurrentQueue<RemuxInput> Inputs { get; } = new();

		public Task<string?> GetFFmpegVersionAsync(CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task<Task<BackgroundTaskSnapshot>> QueueRemuxAsync(RemuxInput input, CancellationToken cancellationToken = default)
		{
			Inputs.Enqueue(input);
			return _queue(input);
		}

		public Task QueueCutAsync(CutInput input, CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}
	}
}
