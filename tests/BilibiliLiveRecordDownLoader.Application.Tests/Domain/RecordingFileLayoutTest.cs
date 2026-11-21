namespace BilibiliLiveRecordDownLoader.Application.Tests.Domain;

public sealed class RecordingFileLayoutTest
{
	private static readonly DateTimeOffset StartTime = new(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(8));

	[Test]
	[Arguments(RecorderType.BuiltIn, StreamFormat.Flv, "20260102_030405.flv")]
	[Arguments(RecorderType.BuiltIn, StreamFormat.Ts, "20260102_030405.ts")]
	[Arguments(RecorderType.BuiltIn, StreamFormat.Fmp4, "20260102_030405_fmp4.mp4")]
	[Arguments(RecorderType.FFmpeg, StreamFormat.Flv, "20260102_030405.ts")]
	[Arguments(RecorderType.FFmpeg, StreamFormat.Fmp4, "20260102_030405.ts")]
	public async Task RecordingPathUsesRoomDirectoryAndFormatExtensionAsync(RecorderType recorderType, StreamFormat format, string expectedFileName)
	{
		string path = RecordingFileLayout.GetRecordingPath(RecordingFileLayout.GetRecordingStem("records", 123, StartTime), format, recorderType);

		await Assert.That(path).IsEqualTo(Path.Combine("records", "123", expectedFileName));
	}

	[Test]
	public async Task SequenceKeepsTheFmp4SuffixLastAsync()
	{
		string path = RecordingFileLayout.GetRecordingPath(RecordingFileLayout.GetRecordingStem("records", 123, StartTime, 2), StreamFormat.Fmp4, RecorderType.BuiltIn);

		await Assert.That(path).IsEqualTo(Path.Combine("records", "123", "20260102_030405_2_fmp4.mp4"));
		await Assert.That(RecordingFileLayout.GetMp4ConversionPath(path)).IsEqualTo(Path.Combine("records", "123", "20260102_030405_2.mp4"));
	}

	[Test]
	[Arguments("a.flv", "a.mp4")]
	[Arguments("a.ts", "a.mp4")]
	[Arguments("a_fmp4.mp4", "a.mp4")]
	[Arguments("a_FMP4.MP4", "a.mp4")]
	public async Task Mp4ConversionPathReplacesRecordingSuffixAsync(string recordingPath, string expected)
	{
		await Assert.That(RecordingFileLayout.GetMp4ConversionPath(recordingPath)).IsEqualTo(expected);
	}

	[Test]
	public async Task PlainMp4NeedsNoConversionAsync()
	{
		await Assert.That(RecordingFileLayout.GetMp4ConversionPath("a.mp4")).IsNull();
	}

	[Test]
	public async Task AnyFileTheStemCanProduceMakesItUnavailableAsync()
	{
		const string stem = "20260102_030405";
		string[] produced =
		[
			.. Enum.GetValues<RecorderType>()
				.SelectMany(_ => Enum.GetValues<StreamFormat>(), (recorderType, format) => RecordingFileLayout.GetRecordingPath(stem, format, recorderType))
				.SelectMany(path => new[] { path, RecordingFileLayout.GetMp4ConversionPath(path) })
				.OfType<string>()
				.Distinct()
		];

		foreach (string path in produced)
		{
			await Assert.That(RecordingFileLayout.IsStemAvailable(stem, existing => existing == path)).IsFalse().Because(path);
		}
	}

	[Test]
	public async Task FlvOnDiskBlocksFmp4WithTheSameRemuxTargetAsync()
	{
		const string stem = "20260102_030405";
		string flv = RecordingFileLayout.GetRecordingPath(stem, StreamFormat.Flv, RecorderType.BuiltIn);
		string fmp4 = RecordingFileLayout.GetRecordingPath(stem, StreamFormat.Fmp4, RecorderType.BuiltIn);

		using (Assert.Multiple())
		{
			await Assert.That(RecordingFileLayout.GetMp4ConversionPath(fmp4)).IsEqualTo(RecordingFileLayout.GetMp4ConversionPath(flv));
			await Assert.That(RecordingFileLayout.IsStemAvailable(stem, existing => existing == flv)).IsFalse();
		}
	}

	[Test]
	[Arguments("20260102_030405_1.flv")]
	[Arguments("20260102_030405.xml")]
	[Arguments("20260102_030405")]
	public async Task UnrelatedFilesKeepTheStemAvailableAsync(string existingFile)
	{
		await Assert.That(RecordingFileLayout.IsStemAvailable("20260102_030405", existing => existing == existingFile)).IsTrue();
	}
}
