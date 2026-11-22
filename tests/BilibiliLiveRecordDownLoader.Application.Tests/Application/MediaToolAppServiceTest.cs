using BilibiliLiveRecordDownLoader.Application.Contracts.Media;
using BilibiliLiveRecordDownLoader.Application.Media;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using BilibiliLiveRecordDownLoader.Domain.Shared.Localization;
using System.Text;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class MediaToolAppServiceTest
{
	private const int TestTimeout = 60_000;
	private const string FailureReason = "muxer does not support the codec";
	private const string OutputPathFile = "output-path.txt";

	// 等的是假 FFmpeg 进程，负载高时启动一个进程就要好几秒
	private static readonly TimeSpan ProcessWaitTimeout = TimeSpan.FromSeconds(30);

	private static readonly byte[] Sps = Convert.FromHexString("6764001FACD9");
	private static readonly byte[] AvcKeyFrame = FlvSamples.Tag(FlvSamples.VideoTag, 0, Convert.FromHexString("1701000000" + "00000005" + "6588840033"));
	private static readonly byte[] AacSequenceHeader = FlvSamples.Tag(FlvSamples.AudioTag, 0, Convert.FromHexString("AF00" + "1210"));
	private static readonly byte[] AacFrame = FlvSamples.Tag(FlvSamples.AudioTag, 23, Convert.FromHexString("AF01" + "21004990"));

	[Test]
	[Timeout(TestTimeout)]
	public async Task FailedRemuxDeletesPartialOutputAndKeepsSourceAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string source = WriteFile(directory, "in.flv", "source"u8.ToArray());
		string destination = directory.Combine("out.mkv");
		MediaToolAppService service = CreateService(directory, exitCode: 1);

		BackgroundTaskSnapshot snapshot = await (await service.QueueRemuxAsync(new RemuxInput(source, destination, true, new RemuxMode.Plain()), cancellationToken)).WaitAsync(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(snapshot.Status).IsEqualTo(BackgroundTaskStatus.Failed);
			await Assert.That(snapshot.Message).IsEqualTo(AppLanguage.Current.FFmpegFailed.ToString(1, FailureReason));
			await Assert.That(File.Exists(destination)).IsFalse();
			await Assert.That(File.Exists(source)).IsTrue();
		}
	}

	[Test]
	[Timeout(TestTimeout)]
	[Arguments(true)]
	[Arguments(false)]
	public async Task SuccessfulRemuxDeletesSourceOnlyWhenAskedAsync(bool deleteSource, CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string source = WriteFile(directory, "in.flv", "source"u8.ToArray());
		string destination = directory.Combine("out.mkv");
		MediaToolAppService service = CreateService(directory, exitCode: 0);

		BackgroundTaskSnapshot snapshot = await (await service.QueueRemuxAsync(new RemuxInput(source, destination, deleteSource, new RemuxMode.Plain()), cancellationToken)).WaitAsync(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(snapshot.Status).IsEqualTo(BackgroundTaskStatus.Completed);
			await Assert.That(File.Exists(destination)).IsTrue();
			await Assert.That(File.Exists(source)).IsEqualTo(!deleteSource);
		}
	}

	[Test]
	[Timeout(TestTimeout)]
	public async Task RepairIsOnlyAppliedToFlvSourcesAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		// 走了修复就会失败
		string source = WriteFile(directory, "in.mp4", "not an flv file"u8.ToArray());
		string destination = directory.Combine("out.mkv");
		MediaToolAppService service = CreateService(directory, exitCode: 0);

		BackgroundTaskSnapshot snapshot = await (await service.QueueRemuxAsync(new RemuxInput(source, destination, false, new RemuxMode.RepairFlv()), cancellationToken)).WaitAsync(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(snapshot.Title).IsEqualTo(AppLanguage.Current.TaskRemux.ToString("in.mp4"));
			await Assert.That(snapshot.Status).IsEqualTo(BackgroundTaskStatus.Completed);
			await Assert.That(File.Exists(destination)).IsTrue();
		}
	}

	[Test]
	[Timeout(TestTimeout)]
	[MethodDataSource(nameof(UnrepairableFlvs))]
	public async Task UnrepairableFlvFailsAndKeepsSourceAsync(byte[] flv, Func<string, string> expectedMessage, CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string source = WriteFile(directory, "live.flv", flv);
		string destination = directory.Combine("out.mp4");
		// FFmpeg 照常成功，丢了流的修复结果也会被当成完成
		MediaToolAppService service = CreateService(directory, exitCode: 0);

		BackgroundTaskSnapshot snapshot = await (await service.QueueRemuxAsync(new RemuxInput(source, destination, true, new RemuxMode.RepairFlv()), cancellationToken)).WaitAsync(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(snapshot.Status).IsEqualTo(BackgroundTaskStatus.Failed);
			await Assert.That(snapshot.Message).IsEqualTo(expectedMessage("live.flv"));
			await Assert.That(File.Exists(source)).IsTrue();
			await Assert.That(File.Exists(destination)).IsFalse();
			await Assert.That(Directory.EnumerateDirectories(directory.Root)).IsEmpty();
		}
	}

	public static IEnumerable<Func<(byte[] Flv, Func<string, string> ExpectedMessage)>> UnrepairableFlvs()
	{
		Func<string, string> unsupportedCodec = static name => AppLanguage.Current.FlvRepairUnsupportedCodec.ToString(name);

		// HEVC 视频只剩音频可拆
		yield return () => ([.. FlvSamples.Header, .. FlvSamples.HevcSequenceHeader(Sps, false), .. FlvSamples.Video(VideoStyle.LegacyHevc, VideoPacket.KeyFrame, 0), .. AacSequenceHeader, .. AacFrame], unsupportedCodec);
		yield return () => ([.. FlvSamples.Header, .. FlvSamples.Video(VideoStyle.EnhancedHevc, VideoPacket.KeyFrame, 0), .. AacSequenceHeader, .. AacFrame], unsupportedCodec);
		// 前半段 H.264、后半段换成 HEVC：两条流都有输出，不报错的话后半段视频就丢了
		yield return () => ([.. FlvSamples.Header, .. FlvSamples.AvcSequenceHeader(Sps), .. AvcKeyFrame, .. AacSequenceHeader, .. AacFrame, .. FlvSamples.HevcSequenceHeader(Sps, false, 1000), .. FlvSamples.Video(VideoStyle.LegacyHevc, VideoPacket.KeyFrame, 1000), .. FlvSamples.Tag(FlvSamples.AudioTag, 1000, Convert.FromHexString("AF01" + "21004990"))], unsupportedCodec);
		// MP3 音频只剩视频可拆
		yield return () => ([.. FlvSamples.Header, .. FlvSamples.AvcSequenceHeader(Sps), .. AvcKeyFrame, .. FlvSamples.Tag(FlvSamples.AudioTag, 0, Convert.FromHexString("2F01" + "21004990"))], unsupportedCodec);
		yield return () => (FlvSamples.Header, static name => AppLanguage.Current.FlvNoSupportedTracks.ToString(name));
		yield return () => ("not an flv file"u8.ToArray(), static name => AppLanguage.Current.NotFlvFile.ToString(name));
	}

	[Test]
	[Arguments(-1, 1000)]
	[Arguments(1000, 1000)]
	[Arguments(2000, 1000)]
	public async Task CutRejectsInvalidTimeRangeAsync(int startMilliseconds, int endMilliseconds, CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		MediaToolAppService service = new(new FFmpegProvider(new FakeConfigManager()), CreateTasks(), NullLogger<MediaToolAppService>.Instance);
		CutInput input = new(directory.Combine("in.mp4"), directory.Combine("out.mp4"), TimeSpan.FromMilliseconds(startMilliseconds), TimeSpan.FromMilliseconds(endMilliseconds));

		ArgumentException? exception = await Assert.That(async () => await service.QueueCutAsync(input, cancellationToken)).ThrowsExactly<ArgumentException>();

		await Assert.That(exception?.Message).IsEqualTo(AppLanguage.Current.InvalidTimeRange);
	}

	[Test]
	[Timeout(TestTimeout)]
	public async Task DestinationDoesNotExistWhileFFmpegIsWritingAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string source = WriteFile(directory, "in.flv", "source"u8.ToArray());
		string destination = directory.Combine("out.mp4");
		BackgroundTaskAppService tasks = CreateTasks();
		MediaToolAppService service = CreateService(directory, exitCode: 0, tasks, hangs: true);

		Task<BackgroundTaskSnapshot> completion = await service.QueueRemuxAsync(new RemuxInput(source, destination, true, new RemuxMode.Plain()), cancellationToken);
		string output = await WaitForOutputPathAsync(directory, cancellationToken);
		bool destinationExistedWhileWriting = File.Exists(destination);
		bool outputExistedWhileWriting = File.Exists(output);
		// 录制和弹幕按主干判断文件是否存在，写到一半的输出不能占住目标的主干
		bool stemAvailableWhileWriting = RecordingFileLayout.IsStemAvailable(RecordingFileLayout.GetStem(destination), File.Exists);
		FileAttributes workDirectoryAttributes = File.GetAttributes(Path.GetDirectoryName(output) ?? string.Empty);

		tasks.Cancel(tasks.GetTasks().Single().Id);
		BackgroundTaskSnapshot snapshot = await completion.WaitAsync(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(destinationExistedWhileWriting).IsFalse();
			await Assert.That(outputExistedWhileWriting).IsTrue();
			await Assert.That(stemAvailableWhileWriting).IsTrue();
			await Assert.That(Path.GetFileName(output)).IsEqualTo("out.mp4");
			await Assert.That(Path.GetDirectoryName(Path.GetDirectoryName(output))).IsEqualTo(directory.Root);
			await Assert.That(Path.GetFileName(Path.GetDirectoryName(output))).StartsWith(".");
			await Assert.That(workDirectoryAttributes.HasFlag(FileAttributes.Hidden)).IsEqualTo(OperatingSystem.IsWindows());
			await Assert.That(snapshot.Status).IsEqualTo(BackgroundTaskStatus.Canceled);
			await Assert.That(File.Exists(destination)).IsFalse();
			await Assert.That(File.Exists(source)).IsTrue();
			await Assert.That(Directory.EnumerateDirectories(directory.Root)).IsEmpty();
		}
	}

	[Test]
	[Timeout(TestTimeout)]
	[Arguments(true)]
	[Arguments(false)]
	public async Task SuccessMovesTheOutputOntoTheDestinationAsync(bool destinationExists, CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string source = WriteFile(directory, "in.flv", "source"u8.ToArray());
		string destination = directory.Combine("out.mp4");

		if (destinationExists)
		{
			WriteFile(directory, "out.mp4", "old"u8.ToArray());
		}

		MediaToolAppService service = CreateService(directory, exitCode: 0);

		BackgroundTaskSnapshot snapshot = await (await service.QueueRemuxAsync(new RemuxInput(source, destination, false, new RemuxMode.Plain()), cancellationToken)).WaitAsync(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(snapshot.Status).IsEqualTo(BackgroundTaskStatus.Completed);
			await Assert.That(await ReadOutputPathAsync(directory, cancellationToken)).IsNotEqualTo(destination);
			await Assert.That((await File.ReadAllTextAsync(destination, cancellationToken)).Trim()).IsEqualTo("partial");
			await Assert.That(Directory.EnumerateDirectories(directory.Root)).IsEmpty();
		}
	}

	[Test]
	[Timeout(TestTimeout)]
	public async Task RemuxDeletesOnlyStaleWorkDirectoriesAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string source = WriteFile(directory, "in.flv", "source"u8.ToArray());
		string destination = directory.Combine("out.mp4");
		string stale = CreateDirectory(directory, ".remux-stale", TimeSpan.FromDays(2));
		string running = CreateDirectory(directory, ".remux-running", TimeSpan.FromHours(1));
		string unrelated = CreateDirectory(directory, ".unrelated", TimeSpan.FromDays(2));
		MediaToolAppService service = CreateService(directory, exitCode: 0);

		BackgroundTaskSnapshot snapshot = await (await service.QueueRemuxAsync(new RemuxInput(source, destination, false, new RemuxMode.Plain()), cancellationToken)).WaitAsync(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(snapshot.Status).IsEqualTo(BackgroundTaskStatus.Completed);
			await Assert.That(Directory.Exists(stale)).IsFalse();
			await Assert.That(Directory.Exists(running)).IsTrue();
			await Assert.That(Directory.Exists(unrelated)).IsTrue();
		}

		static string CreateDirectory(TestDirectory directory, string name, TimeSpan age)
		{
			string path = directory.Combine(name);
			Directory.CreateDirectory(path);
			File.WriteAllText(Path.Combine(path, "partial.mp4"), "partial");
			Directory.SetLastWriteTimeUtc(path, DateTime.UtcNow - age);
			return path;
		}
	}

	[Test]
	[Timeout(TestTimeout)]
	public async Task FailureLeavesAnExistingDestinationUntouchedAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string source = WriteFile(directory, "in.flv", "source"u8.ToArray());
		string destination = WriteFile(directory, "out.mp4", "old"u8.ToArray());
		MediaToolAppService service = CreateService(directory, exitCode: 1);

		BackgroundTaskSnapshot snapshot = await (await service.QueueRemuxAsync(new RemuxInput(source, destination, true, new RemuxMode.Plain()), cancellationToken)).WaitAsync(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(snapshot.Status).IsEqualTo(BackgroundTaskStatus.Failed);
			await Assert.That(await File.ReadAllTextAsync(destination, cancellationToken)).IsEqualTo("old");
			await Assert.That(File.Exists(source)).IsTrue();
			await Assert.That(Directory.EnumerateDirectories(directory.Root)).IsEmpty();
		}
	}

	[Test]
	[Timeout(TestTimeout)]
	public async Task CutWritesBesideTheDestinationFirstAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string source = WriteFile(directory, "in.mp4", "source"u8.ToArray());
		string destination = directory.Combine("cut.mp4");
		BackgroundTaskAppService tasks = CreateTasks();
		MediaToolAppService service = CreateService(directory, exitCode: 0, tasks);

		await service.QueueCutAsync(new CutInput(source, destination, TimeSpan.Zero, TimeSpan.FromSeconds(1)), cancellationToken);
		await TestWait.UntilAsync(() => !tasks.HasActiveTasks, "the cut task finishes", cancellationToken, ProcessWaitTimeout);

		using (Assert.Multiple())
		{
			await Assert.That(tasks.GetTasks().Single().Status).IsEqualTo(BackgroundTaskStatus.Completed);
			await Assert.That(await ReadOutputPathAsync(directory, cancellationToken)).IsNotEqualTo(destination);
			await Assert.That(File.Exists(destination)).IsTrue();
			await Assert.That(Directory.EnumerateDirectories(directory.Root)).IsEmpty();
		}
	}

	[Test]
	[Timeout(TestTimeout)]
	public async Task RepairWritesBesideTheDestinationFirstAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string source = WriteFile(directory, "live.flv", [.. FlvSamples.Header, .. FlvSamples.AvcSequenceHeader(Sps), .. AvcKeyFrame, .. AacSequenceHeader, .. AacFrame]);
		string destination = directory.Combine("out.mp4");
		MediaToolAppService service = CreateService(directory, exitCode: 0);

		BackgroundTaskSnapshot snapshot = await (await service.QueueRemuxAsync(new RemuxInput(source, destination, false, new RemuxMode.RepairFlv()), cancellationToken)).WaitAsync(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(snapshot.Status).IsEqualTo(BackgroundTaskStatus.Completed);
			await Assert.That(await ReadOutputPathAsync(directory, cancellationToken)).IsNotEqualTo(destination);
			await Assert.That(File.Exists(destination)).IsTrue();
			await Assert.That(Directory.EnumerateDirectories(directory.Root)).IsEmpty();
		}
	}

	private static MediaToolAppService CreateService(TestDirectory directory, int exitCode, BackgroundTaskAppService? tasks = null, bool hangs = false)
	{
		AppConfig config = AppConfig.CreateDefault();
		config.FFmpegPath = CreateFakeFFmpeg(directory, exitCode, hangs);
		return new MediaToolAppService(new FFmpegProvider(new FakeConfigManager(config)), tasks ?? CreateTasks(), NullLogger<MediaToolAppService>.Instance);
	}

	private static BackgroundTaskAppService CreateTasks()
	{
		return new BackgroundTaskAppService(TimeProvider.System, NullLogger<BackgroundTaskAppService>.Instance);
	}

	// 和 FFmpeg 一样把最后一个参数当作输出文件
	private static string CreateFakeFFmpeg(TestDirectory directory, int exitCode, bool hangs)
	{
		if (OperatingSystem.IsWindows())
		{
			string wait = hangs ? "ping -n 60 127.0.0.1 >nul\r\n" : string.Empty;
			return WriteFile(directory, "ffmpeg.cmd", Encoding.ASCII.GetBytes($"@echo off\r\nfor %%a in (%*) do set \"last=%%~a\"\r\n>\"%last%\" echo partial\r\n>\"%~dp0{OutputPathFile}\" echo %last%\r\n>&2 echo {FailureReason}\r\n{wait}exit /b {exitCode}\r\n"));
		}

		string sleep = hangs ? "sleep 60\n" : string.Empty;
		string path = WriteFile(directory, "ffmpeg", Encoding.ASCII.GetBytes($"#!/bin/sh\nfor last; do :; done\necho partial > \"$last\"\necho \"$last\" > \"$(dirname \"$0\")/{OutputPathFile}\"\necho '{FailureReason}' >&2\n{sleep}exit {exitCode}\n"));
		File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
		return path;
	}

	private static async Task<string> ReadOutputPathAsync(TestDirectory directory, CancellationToken cancellationToken)
	{
		return (await File.ReadAllTextAsync(directory.Combine(OutputPathFile), cancellationToken)).Trim();
	}

	private static async Task<string> WaitForOutputPathAsync(TestDirectory directory, CancellationToken cancellationToken)
	{
		// 脚本先写输出文件再写路径，路径文件也可能还没写完
		await TestWait.UntilAsync(() => File.Exists(directory.Combine(OutputPathFile)) && new FileInfo(directory.Combine(OutputPathFile)).Length > 0, "the fake FFmpeg writes its output path", cancellationToken, ProcessWaitTimeout);
		return await ReadOutputPathAsync(directory, cancellationToken);
	}

	private static string WriteFile(TestDirectory directory, string name, byte[] content)
	{
		string path = directory.Combine(name);
		File.WriteAllBytes(path, content);
		return path;
	}
}
