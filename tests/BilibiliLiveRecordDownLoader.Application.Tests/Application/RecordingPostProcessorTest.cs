using BilibiliLiveRecordDownLoader.Application.Contracts.Media;
using BilibiliLiveRecordDownLoader.Application.Recording;
using System.Collections.Concurrent;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class RecordingPostProcessorTest
{
	[Test]
	[Arguments(0L)]
	[Arguments(RecordingPostProcessor.MinimumLength - 1)]
	public async Task RecordingWithoutUsableContentIsDeletedInsteadOfConvertedAsync(long length)
	{
		using TestDirectory directory = new();
		string path = CreateRecording(directory, length);
		QueuedRemuxes mediaTools = new();

		await new RecordingPostProcessor(mediaTools, NullLogger<RecordingPostProcessor>.Instance).ProcessAsync(path, CreateProfile(directory));

		using (Assert.Multiple())
		{
			await Assert.That(File.Exists(path)).IsFalse();
			await Assert.That(mediaTools.Inputs).IsEmpty();
		}
	}

	[Test]
	public async Task RecordingWithContentIsQueuedForConversionAsync()
	{
		using TestDirectory directory = new();
		string path = CreateRecording(directory, RecordingPostProcessor.MinimumLength);
		QueuedRemuxes mediaTools = new();

		await new RecordingPostProcessor(mediaTools, NullLogger<RecordingPostProcessor>.Instance).ProcessAsync(path, CreateProfile(directory));

		using (Assert.Multiple())
		{
			await Assert.That(File.Exists(path)).IsTrue();
			await Assert.That(mediaTools.Inputs.Select(static input => input.Source).SequenceEqual([path])).IsTrue();
		}
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
		public ConcurrentQueue<RemuxInput> Inputs { get; } = new();

		public Task<string?> GetFFmpegVersionAsync(CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task QueueRemuxAsync(RemuxInput input, CancellationToken cancellationToken = default)
		{
			Inputs.Enqueue(input);
			return Task.CompletedTask;
		}

		public Task QueueCutAsync(CutInput input, CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}
	}
}
