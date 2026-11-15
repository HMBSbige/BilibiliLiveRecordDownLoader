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
		string path = RecordingFileLayout.CreateRecordingPath("records", 123, StartTime, format, recorderType);

		await Assert.That(path).IsEqualTo(Path.Combine("records", "123", expectedFileName));
	}

	[Test]
	public async Task SequenceKeepsTheFmp4SuffixLastAsync()
	{
		string path = RecordingFileLayout.CreateRecordingPath("records", 123, StartTime, StreamFormat.Fmp4, RecorderType.BuiltIn, 2);

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
}
