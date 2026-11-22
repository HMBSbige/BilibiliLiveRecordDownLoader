using BilibiliLiveRecordDownLoader.Application.Contracts.Media;
using BilibiliLiveRecordDownLoader.Application.Recording;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using System.Collections.Concurrent;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class RecordingPostProcessorTest
{
	private const string Streamer = "streamer";

	[Test]
	[Arguments(0L)]
	[Arguments(RecordingPostProcessor.MinimumLength - 1)]
	public async Task RecordingWithoutUsableContentIsDeletedInsteadOfConvertedAsync(long length)
	{
		using TestDirectory directory = new();
		string path = CreateRecording(directory, length);
		QueuedRemuxes mediaTools = new(BackgroundTaskStatus.Completed);
		RecordingNotifications notifications = new();

		await Create(mediaTools, notifications).ProcessAsync(path, CreateProfile(directory), Streamer);

		using (Assert.Multiple())
		{
			await Assert.That(File.Exists(path)).IsFalse();
			await Assert.That(mediaTools.Inputs).IsEmpty();
			await Assert.That(notifications.Shown).IsEmpty();
		}
	}

	[Test]
	public async Task RecordingWithContentIsQueuedForConversionAsync()
	{
		using TestDirectory directory = new();
		string path = CreateRecording(directory, RecordingPostProcessor.MinimumLength);
		QueuedRemuxes mediaTools = new(BackgroundTaskStatus.Completed);
		RecordingNotifications notifications = new();

		await Create(mediaTools, notifications).ProcessAsync(path, CreateProfile(directory), Streamer);

		using (Assert.Multiple())
		{
			await Assert.That(File.Exists(path)).IsTrue();
			await Assert.That(mediaTools.Inputs.Select(static input => input.Source).SequenceEqual([path])).IsTrue();
			await Assert.That(notifications.Shown).IsEmpty();
		}
	}

	[Test]
	public async Task FailedConversionIsNotifiedWithTheFileNameAsync()
	{
		using TestDirectory directory = new();
		string path = CreateRecording(directory, RecordingPostProcessor.MinimumLength);
		RecordingNotifications notifications = new();

		await Create(new QueuedRemuxes(BackgroundTaskStatus.Failed, "Invalid data found"), notifications).ProcessAsync(path, CreateProfile(directory), Streamer);

		ShownNotification shown = await Assert.That(notifications.Shown).HasSingleItem();

		using (Assert.Multiple())
		{
			await Assert.That(shown.Title).Contains(Streamer);
			await Assert.That(shown.Message).Contains(Path.GetFileName(path));
			await Assert.That(shown.Message).Contains("Invalid data found");
			await Assert.That(shown.Type).IsEqualTo(AppNotificationType.Error);
		}
	}

	[Test]
	public async Task ConversionThatCannotBeQueuedIsNotifiedAsync()
	{
		using TestDirectory directory = new();
		string path = CreateRecording(directory, RecordingPostProcessor.MinimumLength);
		RecordingNotifications notifications = new();

		await Create(new QueuedRemuxes(new FileNotFoundException("FFmpeg is missing")), notifications).ProcessAsync(path, CreateProfile(directory), Streamer);

		ShownNotification shown = await Assert.That(notifications.Shown).HasSingleItem();
		await Assert.That(shown.Message).Contains("FFmpeg is missing");
	}

	[Test]
	public async Task CanceledConversionIsNotNotifiedAsync()
	{
		using TestDirectory directory = new();
		string path = CreateRecording(directory, RecordingPostProcessor.MinimumLength);
		RecordingNotifications notifications = new();

		await Create(new QueuedRemuxes(BackgroundTaskStatus.Canceled), notifications).ProcessAsync(path, CreateProfile(directory), Streamer);

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

		await Create(new QueuedRemuxes(BackgroundTaskStatus.Failed, "boom"), notifications, config).ProcessAsync(path, CreateProfile(directory), Streamer);

		await Assert.That(notifications.Shown).IsEmpty();
	}

	private static RecordingPostProcessor Create(IMediaToolAppService mediaTools, RecordingNotifications notifications, AppConfig? config = null)
	{
		return new RecordingPostProcessor(mediaTools, new FakeConfigManager(config), notifications, NullLogger<RecordingPostProcessor>.Instance);
	}

	private static string CreateRecording(TestDirectory directory, long length)
	{
		string path = directory.Combine("20260102_030405.flv");
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
