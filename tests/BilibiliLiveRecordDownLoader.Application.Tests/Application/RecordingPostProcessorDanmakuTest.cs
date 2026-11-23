using BilibiliLive.Danmu;
using BilibiliLiveRecordDownLoader.Application.Contracts.Media;
using BilibiliLiveRecordDownLoader.Application.Danmaku;
using BilibiliLiveRecordDownLoader.Application.Recording;
using BilibiliLiveRecordDownLoader.Application.Tests.Danmaku;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using BilibiliLiveRecordDownLoader.Domain.Shared.Localization;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class RecordingPostProcessorDanmakuTest
{
	private const string Stem = "20260102_030405";

	[Test]
	public async Task TooSmallRecordingTakesItsDanmakuAndSubtitleAlongAsync()
	{
		using TestDirectory directory = new();
		string path = CreateRecording(directory, RecordingPostProcessor.MinimumLength - 1);
		await File.WriteAllTextAsync(directory.Combine(Stem + ".xml"), "x");
		await File.WriteAllTextAsync(directory.Combine(Stem + ".ass"), "x");
		await File.WriteAllTextAsync(directory.Combine(Stem + ".danmaku.jsonl"), "x");

		await Create(new RecordingMediaTools()).ProcessAsync(path, CreateProfile(directory), "streamer", new DanmakuSegment(directory.Combine(Stem + ".xml"), null, null), CreateDanmakuProfile());

		using (Assert.Multiple())
		{
			await Assert.That(File.Exists(path)).IsFalse();
			await Assert.That(File.Exists(directory.Combine(Stem + ".xml"))).IsFalse();
			await Assert.That(File.Exists(directory.Combine(Stem + ".ass"))).IsFalse();
			// 原始消息属于整场直播
			await Assert.That(File.Exists(directory.Combine(Stem + ".danmaku.jsonl"))).IsTrue();
		}
	}

	[Test]
	public async Task SubtitleIsGeneratedAtTheVideoSizeAndChaptersGoIntoTheConversionAsync()
	{
		using TestDirectory directory = new();
		string path = CreateRecording(directory, RecordingPostProcessor.MinimumLength);
		string danmaku = await WriteDanmakuAsync(directory, [DanmakuSamples.Comment(10), new DanmakuRecord.TitleChange(TimeSpan.FromSeconds(100), "新标题")]);
		RecordingMediaTools mediaTools = new();

		await Create(mediaTools).ProcessAsync(path, CreateProfile(directory), "streamer", new DanmakuSegment(danmaku, new VideoResolution(1280, 720), TimeSpan.FromSeconds(200)), CreateDanmakuProfile(writeChapters: true));

		RemuxInput remux = mediaTools.Remuxes.Single();
		string chapters = mediaTools.Chapters[remux.Destination];

		using (Assert.Multiple())
		{
			await Assert.That(await File.ReadAllTextAsync(directory.Combine(Stem + ".ass"))).Contains("PlayResY: 720");
			await Assert.That(chapters).Contains("START=0\nEND=100000\ntitle=测试标题\n");
			await Assert.That(chapters).Contains($"START=100000\nEND=200000\ntitle={AppLanguage.Current.ChapterTitleChange.ToString("新标题")}\n");
			await Assert.That(remux.Mode is RemuxMode.WithChapters { Path: var chaptersPath } && !File.Exists(chaptersPath)).IsTrue();
		}
	}

	[Test]
	public async Task NothingExtraIsWrittenWhenTurnedOffAsync()
	{
		using TestDirectory directory = new();
		string path = CreateRecording(directory, RecordingPostProcessor.MinimumLength);
		string danmaku = await WriteDanmakuAsync(directory, [DanmakuSamples.Comment(10)]);
		RecordingMediaTools mediaTools = new();

		await Create(mediaTools).ProcessAsync(path, CreateProfile(directory), "streamer", new DanmakuSegment(danmaku, null, null), CreateDanmakuProfile(generateSubtitle: false));

		using (Assert.Multiple())
		{
			await Assert.That(File.Exists(directory.Combine(Stem + ".ass"))).IsFalse();
			await Assert.That(mediaTools.Remuxes.Single().Mode).IsEqualTo(new RemuxMode.Plain());
		}
	}

	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task PaidMessagesInTheSubtitleFollowTheSettingAsync(bool show)
	{
		using TestDirectory directory = new();
		string path = CreateRecording(directory, RecordingPostProcessor.MinimumLength);
		string danmaku = await WriteDanmakuAsync(directory, [DanmakuSamples.Comment(10), new DanmakuRecord.Guard(TimeSpan.FromSeconds(20), 1, "舰长", GuardLevel.Captain, 3, 594000)]);

		await Create(new RecordingMediaTools()).ProcessAsync(path, CreateProfile(directory), "streamer", new DanmakuSegment(danmaku, null, null), CreateDanmakuProfile(showPaidMessages: show));

		string subtitle = await File.ReadAllTextAsync(directory.Combine(Stem + ".ass"));

		using (Assert.Multiple())
		{
			await Assert.That(subtitle.Contains(",Message,,", StringComparison.Ordinal)).IsEqualTo(show);
			await Assert.That(subtitle.Contains(DanmakuTexts.SubtitleLabels.Guard("舰长", GuardLevel.Captain, 3, false), StringComparison.Ordinal)).IsEqualTo(show);
		}
	}

	[Test]
	public async Task ChaptersNeedTheConversionAsync()
	{
		using TestDirectory directory = new();
		string path = CreateRecording(directory, RecordingPostProcessor.MinimumLength);
		string danmaku = await WriteDanmakuAsync(directory, [DanmakuSamples.Comment(10)]);
		RecordingMediaTools mediaTools = new();
		AppConfig config = AppConfig.CreateDefault();
		config.RecordDirectory = directory.Root;

		await Create(mediaTools).ProcessAsync(path, RecordingProfile.Create(config, new RoomConfig { RoomId = 1 }), "streamer", new DanmakuSegment(danmaku, null, null), CreateDanmakuProfile(writeChapters: true));

		using (Assert.Multiple())
		{
			await Assert.That(mediaTools.Remuxes).IsEmpty();
			await Assert.That(File.Exists(directory.Combine(Stem + ".ass"))).IsTrue();
			await Assert.That(Directory.GetFiles(directory.Root, "*.ffmetadata")).IsEmpty();
		}
	}

	[Test]
	public async Task ChaptersThatCannotBeWrittenAreLeftOutOfTheConversionAsync()
	{
		using TestDirectory directory = new();
		string path = CreateRecording(directory, RecordingPostProcessor.MinimumLength);
		string danmaku = await WriteDanmakuAsync(directory, [DanmakuSamples.Comment(10)]);
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

		await processor.ProcessAsync(path, CreateProfile(directory), "streamer", new DanmakuSegment(danmaku, null, TimeSpan.FromSeconds(60)), CreateDanmakuProfile(writeChapters: true));

		using (Assert.Multiple())
		{
			await Assert.That(mediaTools.Remuxes.Single().Mode).IsEqualTo(new RemuxMode.Plain());
			await Assert.That(File.Exists(directory.Combine(Stem + ".ass"))).IsTrue();
			await Assert.That(Directory.GetFiles(directory.Root, "*.ffmetadata")).IsEmpty();
			await Assert.That(notifications.Shown).IsEmpty();
		}
	}

	private static RecordingPostProcessor Create(RecordingMediaTools mediaTools)
	{
		return new RecordingPostProcessor(mediaTools, new FakeConfigManager(), new RecordingNotifications(), NullLogger<RecordingPostProcessor>.Instance);
	}

	private static DanmakuProfile CreateDanmakuProfile(bool generateSubtitle = true, bool writeChapters = false, bool showPaidMessages = true)
	{
		return new DanmakuProfile(true, true, DanmakuGiftMode.None, true, TimeSpan.Zero, generateSubtitle, showPaidMessages, false, writeChapters);
	}

	private static async Task<string> WriteDanmakuAsync(TestDirectory directory, IEnumerable<DanmakuRecord> records)
	{
		string path = directory.Combine(Stem + ".xml");
		await File.WriteAllBytesAsync(path, await DanmakuSamples.WriteAsync(records));
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
}
