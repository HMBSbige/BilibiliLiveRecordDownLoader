using MediaToolkit.Flv;

namespace MediaToolkit.Tests.Flv;

public class FlvExtractorTest
{
	private static readonly byte[] AacPayload = FlvSamples.AacFrame.AsSpan(2).ToArray();

	private static byte[] BuildAudioVideoFlv()
	{
		return new FlvBuilder()
			.Tag(FlvTagType.Video, 0, FlvSamples.AvcSequenceHeader)
			.Tag(FlvTagType.Audio, 0, FlvSamples.AacSequenceHeader)
			.Tag(FlvTagType.Video, 0, FlvSamples.AvcKeyFrame)
			.Tag(FlvTagType.Audio, 23, FlvSamples.AacFrame)
			.Tag(FlvTagType.Audio, 46, FlvSamples.AacFrame)
			.ToArray();
	}

	[Test]
	public async Task ExtractsAnnexBVideoAndAdtsAudioAsync()
	{
		using TestDirectory directory = new();
		string input = directory.CreateFile(@"live.flv", BuildAudioVideoFlv());
		string output = directory.Combine(@"out");

		FlvExtractResult result = await FlvExtractor.ExtractAsync(input, output);

		await Assert.That(result).IsEqualTo(new FlvExtractResult(Path.Combine(output, @"live.h264"), Path.Combine(output, @"live.aac")));

		byte[] expectedVideo = FlvSamples.AnnexB(FlvSamples.Sps, FlvSamples.Pps, FlvSamples.IdrSlice, FlvSamples.Sei);
		await Assert.That(Convert.ToHexString(await File.ReadAllBytesAsync(Path.Combine(output, @"live.h264")))).IsEqualTo(Convert.ToHexString(expectedVideo));

		byte[] expectedAudio = [.. FlvSamples.AdtsHeaderForAacFrame, .. AacPayload, .. FlvSamples.AdtsHeaderForAacFrame, .. AacPayload];
		await Assert.That(Convert.ToHexString(await File.ReadAllBytesAsync(Path.Combine(output, @"live.aac")))).IsEqualTo(Convert.ToHexString(expectedAudio));
	}

	[Test]
	public async Task EstimatesAverageFrameRateFromVideoTimestampsAsync()
	{
		using TestDirectory directory = new();
		FlvBuilder builder = new FlvBuilder().Tag(FlvTagType.Video, 0, FlvSamples.AvcSequenceHeader);

		// 30 帧跨 1 秒，中间丢掉一帧也只影响平均值
		foreach (uint timestamp in Enumerable.Range(0, 31).Where(static frame => frame is not 15).Select(static frame => (uint)(frame * 1000 / 30)))
		{
			builder.Tag(FlvTagType.Video, timestamp, FlvSamples.AvcKeyFrame);
		}

		string input = directory.CreateFile(@"live.flv", builder.ToArray());

		FlvExtractResult result = await FlvExtractor.ExtractAsync(input, directory.Combine(@"out"));

		await Assert.That(result.VideoFrameRate).IsEqualTo(29.0);
	}

	[Test]
	public async Task FrameRateIsUnknownWithASingleFrameAsync()
	{
		using TestDirectory directory = new();
		string input = directory.CreateFile(@"live.flv", BuildAudioVideoFlv());

		FlvExtractResult result = await FlvExtractor.ExtractAsync(input, directory.Combine(@"out"));

		await Assert.That(result.VideoFrameRate).IsNull();
	}

	[Test]
	public async Task ReportsMonotonicProgressEndingAtOneAsync()
	{
		using TestDirectory directory = new();
		string input = directory.CreateFile(@"live.flv", BuildAudioVideoFlv());
		List<double> values = [];

		await FlvExtractor.ExtractAsync(input, directory.Root, new SyncProgress<double>(values.Add));

		await Assert.That(values).IsNotEmpty();
		await Assert.That(values).IsInOrder();
		await Assert.That(values[0]).IsGreaterThan(0);
		await Assert.That(values[^1]).IsEqualTo(1);
	}

	[Test]
	public async Task MissingAudioYieldsNullPathWithoutFileAsync()
	{
		using TestDirectory directory = new();
		byte[] flv = new FlvBuilder(FlvHeaderFlags.Video)
			.Tag(FlvTagType.Video, 0, FlvSamples.AvcSequenceHeader)
			.Tag(FlvTagType.Video, 0, FlvSamples.AvcKeyFrame)
			.ToArray();
		string input = directory.CreateFile(@"video.flv", flv);

		FlvExtractResult result = await FlvExtractor.ExtractAsync(input, directory.Root);

		await Assert.That(result.VideoPath).IsEqualTo(directory.Combine(@"video.h264"));
		await Assert.That(result.AudioPath).IsNull();
		await Assert.That(File.Exists(directory.Combine(@"video.aac"))).IsFalse();
	}

	[Test]
	public async Task UnsupportedVideoCodecYieldsNullPathWithoutFileAsync()
	{
		using TestDirectory directory = new();
		// CodecID 12：B 站 HEVC 流使用的非标准编号
		byte[] flv = new FlvBuilder()
			.Tag(FlvTagType.Video, 0, Convert.FromHexString(@"1C00000000" + @"01"))
			.Tag(FlvTagType.Video, 0, Convert.FromHexString(@"1C01000000" + @"000000032601AF"))
			.Tag(FlvTagType.Audio, 0, FlvSamples.AacSequenceHeader)
			.Tag(FlvTagType.Audio, 0, FlvSamples.AacFrame)
			.ToArray();
		string input = directory.CreateFile(@"hevc.flv", flv);

		FlvExtractResult result = await FlvExtractor.ExtractAsync(input, directory.Root);

		await Assert.That(result.VideoPath).IsNull();
		await Assert.That(File.Exists(directory.Combine(@"hevc.h264"))).IsFalse();
		await Assert.That(result.AudioPath).IsEqualTo(directory.Combine(@"hevc.aac"));
	}

	[Test]
	public async Task TruncatedLastTagIsIgnoredAsync()
	{
		using TestDirectory directory = new();
		byte[] flv = new FlvBuilder()
			.Tag(FlvTagType.Audio, 0, FlvSamples.AacSequenceHeader)
			.Tag(FlvTagType.Audio, 0, FlvSamples.AacFrame)
			.Tag(FlvTagType.Audio, 23, FlvSamples.AacFrame)
			.ToArray();
		// 去掉最后的 PreviousTagSize 和最后一帧的 2 个字节
		string input = directory.CreateFile(@"cut.flv", flv.AsSpan(0, flv.Length - sizeof(uint) - 2).ToArray());

		FlvExtractResult result = await FlvExtractor.ExtractAsync(input, directory.Root);

		await Assert.That(result.AudioPath).IsNotNull();
		byte[] expected = [.. FlvSamples.AdtsHeaderForAacFrame, .. AacPayload];
		await Assert.That(Convert.ToHexString(await File.ReadAllBytesAsync(directory.Combine(@"cut.aac")))).IsEqualTo(Convert.ToHexString(expected));
	}

	[Test]
	public async Task SkipsExtendedHeaderUsingDataOffsetAsync()
	{
		using TestDirectory directory = new();
		byte[] flv = new FlvBuilder().Tag(FlvTagType.Audio, 0, FlvSamples.AacSequenceHeader).Tag(FlvTagType.Audio, 0, FlvSamples.AacFrame).ToArray();
		// DataOffset 改为 13，在头后面插入 4 个垃圾字节
		byte[] extended = [.. flv.AsSpan(0, 8), 13, 0xDE, 0xAD, 0xBE, 0xEF, .. flv.AsSpan(FlvHeader.Size)];
		string input = directory.CreateFile(@"extended.flv", extended);

		FlvExtractResult result = await FlvExtractor.ExtractAsync(input, directory.Root);

		await Assert.That(result.AudioPath).IsNotNull();
		await Assert.That(new FileInfo(directory.Combine(@"extended.aac")).Length).IsEqualTo(FlvSamples.AdtsHeaderForAacFrame.Length + AacPayload.Length);
	}

	[Test]
	public async Task NonFlvInputThrowsWithoutOutputAsync()
	{
		using TestDirectory directory = new();
		string input = directory.CreateFile(@"fake.flv", "not an flv file"u8.ToArray());

		await Assert.That(async () => await FlvExtractor.ExtractAsync(input, directory.Root)).Throws<InvalidDataException>();
		string[] expected = [input];
		await Assert.That(Directory.EnumerateFiles(directory.Root)).IsEquivalentTo(expected, StringComparer.Ordinal, CollectionOrdering.Matching);
	}

	[Test]
	public async Task RefusesToOverwriteInputAsync()
	{
		using TestDirectory directory = new();
		string input = directory.CreateFile(@"stream.aac", BuildAudioVideoFlv());

		await Assert.That(async () => await FlvExtractor.ExtractAsync(input, directory.Root)).Throws<ArgumentException>();
		await Assert.That(Convert.ToHexString(await File.ReadAllBytesAsync(input))).IsEqualTo(Convert.ToHexString(BuildAudioVideoFlv()));
	}

	[Test]
	public async Task CancellationDeletesPartialOutputAsync()
	{
		using TestDirectory directory = new();
		FlvBuilder builder = new FlvBuilder().Tag(FlvTagType.Audio, 0, FlvSamples.AacSequenceHeader);

		for (uint i = 0; i < 1000; ++i)
		{
			builder.Tag(FlvTagType.Audio, i * 23, FlvSamples.AacFrame);
		}

		string input = directory.CreateFile(@"long.flv", builder.ToArray());
		using CancellationTokenSource cts = new();
		bool audioWritten = false;
		string audioPath = directory.Combine(@"long.aac");

		SyncProgress<double> progress = new(value =>
		{
			if (value > 0.5 && !cts.IsCancellationRequested)
			{
				audioWritten = File.Exists(audioPath);
				cts.Cancel();
			}
		});

		await Assert.That(async () => await FlvExtractor.ExtractAsync(input, directory.Root, progress, cts.Token)).Throws<OperationCanceledException>();
		await Assert.That(audioWritten).IsTrue();
		await Assert.That(File.Exists(audioPath)).IsFalse();
	}
}
