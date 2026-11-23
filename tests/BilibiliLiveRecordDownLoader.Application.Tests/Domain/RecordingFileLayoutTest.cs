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
		string path = RecordingFileLayout.GetRecordingPath(RecordingFileLayout.GetRecordingStem("records", 123, StartTime, null), format, recorderType);

		await Assert.That(path).IsEqualTo(Path.Combine("records", "123", expectedFileName));
	}

	[Test]
	[Arguments(null, "20260102_030405_2")]
	[Arguments("今晚唱歌", "20260102_030405_今晚唱歌_2")]
	public async Task SequenceFollowsTheTitleAndPrecedesTheFmp4SuffixAsync(string? title, string expectedStem)
	{
		string path = RecordingFileLayout.GetRecordingPath(RecordingFileLayout.GetRecordingStem("records", 123, StartTime, title, 2), StreamFormat.Fmp4, RecorderType.BuiltIn);
		string stem = Path.Combine("records", "123", expectedStem);

		using (Assert.Multiple())
		{
			await Assert.That(path).IsEqualTo(stem + "_fmp4.mp4");
			await Assert.That(RecordingFileLayout.GetMp4ConversionPath(path)).IsEqualTo(stem + ".mp4");
		}
	}

	[Test]
	[Arguments("a/b\\c:d*e?f\"g<h>i|j", "20260102_030405_a_b_c_d_e_f_g_h_i_j")]
	[Arguments("  第一行\n第二行\t ", "20260102_030405_第一行_第二行")]
	[Arguments(null, "20260102_030405")]
	[Arguments(" \t\n", "20260102_030405")]
	public async Task TitleIsCleanedForEveryFileSystemAsync(string? title, string expectedFileName)
	{
		await Assert.That(Path.GetFileName(RecordingFileLayout.GetRecordingStem("records", 123, StartTime, title))).IsEqualTo(expectedFileName);
	}

	[Test]
	public async Task UnpairedSurrogateInTheTitleBecomesTheReplacementCharacterAsync()
	{
		await Assert.That(Path.GetFileName(RecordingFileLayout.GetRecordingStem("records", 123, StartTime, "\uD800x"))).IsEqualTo("20260102_030405_\uFFFDx");
	}

	[Test]
	public async Task LongTitleIsCutAtWholeCharactersWithinTheByteLimitAsync()
	{
		// 38 个汉字是 114 字节，带肤色的 emoji 是两个码位共 8 字节，只放得下前一个码位
		string cjk = Path.GetFileName(RecordingFileLayout.GetRecordingStem("records", 123, StartTime, new string('啊', 38) + "👍🏽尾"));
		string ascii = Path.GetFileName(RecordingFileLayout.GetRecordingStem("records", 123, StartTime, new string('a', 200)));

		using (Assert.Multiple())
		{
			await Assert.That(cjk).IsEqualTo("20260102_030405_" + new string('啊', 38));
			await Assert.That(ascii).IsEqualTo("20260102_030405_" + new string('a', 120));
		}
	}

	[Test]
	[Arguments("a.flv", "a.mp4")]
	[Arguments("a_fmp4.mp4", "a.mp4")]
	[Arguments("a_FMP4.MP4", "a.mp4")]
	[Arguments("a.mp4", null)]
	public async Task Mp4ConversionPathReplacesRecordingSuffixAsync(string recordingPath, string? expected)
	{
		await Assert.That(RecordingFileLayout.GetMp4ConversionPath(recordingPath)).IsEqualTo(expected);
	}

	[Test]
	public async Task AnyFileTheStemCanProduceMakesItUnavailableAsync()
	{
		const string stem = "20260102_030405";
		string[] produced =
		[
			.. Enum.GetValues<RecorderType>()
				.SelectMany(static _ => Enum.GetValues<StreamFormat>(), static (recorderType, format) => RecordingFileLayout.GetRecordingPath(stem, format, recorderType))
				.SelectMany(static path => new[] { path, RecordingFileLayout.GetMp4ConversionPath(path) })
				.OfType<string>()
				.Distinct()
		];

		foreach (string path in produced)
		{
			await Assert.That(RecordingFileLayout.IsStemAvailable(stem, existing => existing == path)).IsFalse().Because(path);
		}
	}

	[Test]
	public async Task RecordingsWhoseConversionLandsOnTheFmp4FileMakeTheStemUnavailableAsync()
	{
		string stem = RecordingFileLayout.GetRecordingStem("records", 123, StartTime, "a");
		string fmp4 = RecordingFileLayout.GetRecordingPath(stem, StreamFormat.Fmp4, RecorderType.BuiltIn);
		string titledStem = RecordingFileLayout.GetRecordingStem("records", 123, StartTime, "a_fmp4");
		string[] recordings = [.. Enum.GetValues<StreamFormat>().Select(format => RecordingFileLayout.GetRecordingPath(titledStem, format, RecorderType.BuiltIn)).Distinct()];

		foreach (string recording in recordings)
		{
			using (Assert.Multiple())
			{
				await Assert.That(RecordingFileLayout.GetMp4ConversionPath(recording)).IsEqualTo(fmp4).Because(recording);
				await Assert.That(RecordingFileLayout.IsStemAvailable(stem, existing => existing == recording)).IsFalse().Because(recording);
			}
		}
	}

	[Test]
	[Arguments("20260102_030405_1.flv")]
	[Arguments("20260102_030405.txt")]
	[Arguments("20260102_030405")]
	public async Task UnrelatedFilesKeepTheStemAvailableAsync(string existingFile)
	{
		await Assert.That(RecordingFileLayout.IsStemAvailable("20260102_030405", existing => existing == existingFile)).IsTrue();
	}

	[Test]
	[Arguments(".xml")]
	[Arguments(".ass")]
	[Arguments(".danmaku.jsonl")]
	public async Task DanmakuFilesOccupyTheirStemAsync(string suffix)
	{
		string stem = RecordingFileLayout.GetRecordingStem("records", 123, StartTime, null);

		await Assert.That(RecordingFileLayout.IsStemAvailable(stem, path => path == stem + suffix)).IsFalse();
	}

	[Test]
	[Arguments("20260102_030405.flv", "20260102_030405")]
	[Arguments("20260102_030405_fmp4.mp4", "20260102_030405")]
	[Arguments("20260102_030405.mp4", "20260102_030405")]
	[Arguments("20260102_030405_v1.5.ts", "20260102_030405_v1.5")]
	[Arguments("20260102_030405.danmaku.jsonl", "20260102_030405")]
	public async Task StemIsThePathWithoutTheRecordingSuffixAsync(string fileName, string expectedStem)
	{
		await Assert.That(RecordingFileLayout.GetStem(Path.Combine("records", "123", fileName))).IsEqualTo(Path.Combine("records", "123", expectedStem));
	}

	[Test]
	public async Task VideosOfAStemAreLookedUpWithTheConvertedOneFirstAsync()
	{
		await Assert.That(RecordingFileLayout.GetVideoPaths("a")).IsEquivalentTo(["a.mp4", "a_fmp4.mp4", "a.flv", "a.ts"], CollectionOrdering.Matching);
	}
}
