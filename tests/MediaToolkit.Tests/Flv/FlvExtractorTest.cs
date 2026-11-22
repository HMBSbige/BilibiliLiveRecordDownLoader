using MediaToolkit.Flv;
using System.Buffers.Binary;

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

		// 丢掉一帧后剩 29 个间隔，仍跨 1 秒
		foreach (uint timestamp in Enumerable.Range(0, 31).Where(static frame => frame is not 15).Select(static frame => (uint)(frame * 1000 / 30)))
		{
			builder.Tag(FlvTagType.Video, timestamp, FlvSamples.AvcKeyFrame);
		}

		string input = directory.CreateFile(@"live.flv", builder.ToArray());

		FlvExtractResult result = await FlvExtractor.ExtractAsync(input, directory.Combine(@"out"));

		await Assert.That(result.VideoFrameRate).IsEqualTo(29.0);
	}

	[Test]
	[Arguments(0u, "timestamps restart from zero")]
	[Arguments(600_000u, "timestamps jump ten minutes ahead")]
	[Arguments(2500u, "timestamps step back half a second")]
	public async Task FrameRateIgnoresTimestampDiscontinuitiesAsync(uint secondHalfStart, string reason)
	{
		using TestDirectory directory = new();
		FlvBuilder builder = new FlvBuilder().Tag(FlvTagType.Video, 0, FlvSamples.AvcSequenceHeader);

		foreach (uint timestamp in Enumerable.Range(0, 60).Select(static frame => (uint)(frame * 1000 / 30)))
		{
			builder.Tag(FlvTagType.Video, 1000 + timestamp, FlvSamples.AvcKeyFrame);
		}

		foreach (uint timestamp in Enumerable.Range(0, 60).Select(static frame => (uint)(frame * 1000 / 30)))
		{
			builder.Tag(FlvTagType.Video, secondHalfStart + timestamp, FlvSamples.AvcKeyFrame);
		}

		string input = directory.CreateFile(@"live.flv", builder.ToArray());

		FlvExtractResult result = await FlvExtractor.ExtractAsync(input, directory.Combine(@"out"));

		await Assert.That(result.VideoFrameRate ?? 0).IsBetween(29.9, 30.1).Because(reason);
	}

	[Test]
	public async Task FrameRateCountsOnlyTagsThatProduceFramesAsync()
	{
		using TestDirectory directory = new();
		FlvBuilder builder = new FlvBuilder().Tag(FlvTagType.Video, 0, FlvSamples.AvcSequenceHeader);

		foreach (uint timestamp in Enumerable.Range(0, 31).Select(static frame => (uint)(frame * 1000 / 30)))
		{
			builder.Tag(FlvTagType.Video, timestamp, FlvSamples.AvcKeyFrame);
			// 信息帧和长度越界的 NALU
			builder.Tag(FlvTagType.Video, timestamp + 1, Convert.FromHexString(@"5701000000" + @"00000005" + @"6588840033"));
			builder.Tag(FlvTagType.Video, timestamp + 2, Convert.FromHexString(@"2701000000" + @"00000009" + @"060501"));
		}

		string input = directory.CreateFile(@"live.flv", builder.ToArray());

		FlvExtractResult result = await FlvExtractor.ExtractAsync(input, directory.Combine(@"out"));

		await Assert.That(result.VideoFrameRate).IsEqualTo(30.0);
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
	// CodecID 12：B 站 HEVC 流使用的非标准编号
	[Arguments(false, true, @"1C00000000" + @"01", "video starts as HEVC")]
	[Arguments(false, true, @"1700000000" + @"0164001FFFE1" + @"0004" + @"6764001F", "video sequence header ends after its SPS")]
	[Arguments(false, false, @"2F01" + @"21004990", "audio starts as MP3")]
	// AudioObjectType 6（BSAC）无法用 ADTS 表示
	[Arguments(false, false, @"AF00" + @"3210", "audio starts with an unsupported AudioSpecificConfig")]
	[Arguments(true, true, @"1C00000000" + @"01", "video switches to HEVC")]
	[Arguments(true, true, @"9068766331" + @"01", "video switches to enhanced HEVC")]
	[Arguments(true, true, @"1700000000" + @"00", "video sequence header cannot be parsed")]
	[Arguments(true, false, @"2F01" + @"21004990", "audio switches to MP3")]
	[Arguments(true, false, @"AF00" + @"3210", "audio switches to an unsupported AudioSpecificConfig")]
	public async Task UnconvertibleTagThrowsWithoutOutputAsync(bool midway, bool isVideo, string hex, string reason)
	{
		using TestDirectory directory = new();
		byte[] flv = (midway ? BuildConvertibleStart(4) : new FlvBuilder())
			.Tag(isVideo ? FlvTagType.Video : FlvTagType.Audio, 1000, Convert.FromHexString(hex))
			.Tag(FlvTagType.Video, 1000, FlvSamples.AvcSequenceHeader)
			.Tag(FlvTagType.Video, 1000, FlvSamples.AvcKeyFrame)
			.Tag(FlvTagType.Audio, 1000, FlvSamples.AacSequenceHeader)
			.Tag(FlvTagType.Audio, 1000, FlvSamples.AacFrame)
			.ToArray();
		string input = directory.CreateFile(@"input.flv", flv);

		await Assert.That(async () => await FlvExtractor.ExtractAsync(input, directory.Root)).Throws<NotSupportedException>().Because(reason);
		string[] expected = [input];
		await Assert.That(Directory.EnumerateFiles(directory.Root)).IsEquivalentTo(expected, StringComparer.Ordinal, CollectionOrdering.Matching);
	}

	[Test]
	public async Task TagsWithoutMediaDataMidwayAreToleratedAsync()
	{
		using TestDirectory directory = new();
		byte[] flv = BuildConvertibleStart(2)
			.Tag(FlvTagType.Video, 500, Convert.FromHexString(@"5701000000" + @"00000005" + @"6588840033"))
			.Tag(FlvTagType.Video, 500, Convert.FromHexString(@"2701000000" + @"00000009" + @"060501"))
			.Tag(FlvTagType.Video, 500, FlvSamples.AvcSequenceHeader)
			.Tag(FlvTagType.Audio, 500, FlvSamples.AacSequenceHeader)
			.Tag(FlvTagType.Audio, 500, Convert.FromHexString(@"AF01"))
			.Tag(FlvTagType.Video, 533, FlvSamples.AvcKeyFrame)
			.Tag(FlvTagType.Audio, 533, FlvSamples.AacFrame)
			.Tag(FlvTagType.Video, 566, Convert.FromHexString(@"1702000000"))
			.ToArray();
		string input = directory.CreateFile(@"live.flv", flv);

		FlvExtractResult result = await FlvExtractor.ExtractAsync(input, directory.Root);

		await Assert.That(result.VideoPath).IsEqualTo(directory.Combine(@"live.h264"));
		await Assert.That(result.AudioPath).IsEqualTo(directory.Combine(@"live.aac"));
	}

	private static FlvBuilder BuildConvertibleStart(int frames)
	{
		FlvBuilder builder = new FlvBuilder()
			.Tag(FlvTagType.Video, 0, FlvSamples.AvcSequenceHeader)
			.Tag(FlvTagType.Audio, 0, FlvSamples.AacSequenceHeader);

		for (uint i = 0; i < frames; ++i)
		{
			builder.Tag(FlvTagType.Video, i * 33, FlvSamples.AvcKeyFrame).Tag(FlvTagType.Audio, i * 23, FlvSamples.AacFrame);
		}

		return builder;
	}

	[Test]
	public async Task EmptyTagsDoNotCountAsAStreamAsync()
	{
		using TestDirectory directory = new();
		byte[] flv = new FlvBuilder()
			.Tag(FlvTagType.Video, 0)
			.Tag(FlvTagType.Audio, 0, FlvSamples.AacSequenceHeader)
			.Tag(FlvTagType.Audio, 0, FlvSamples.AacFrame)
			.ToArray();
		string input = directory.CreateFile(@"audio.flv", flv);

		FlvExtractResult result = await FlvExtractor.ExtractAsync(input, directory.Root);

		await Assert.That(result).IsEqualTo(new FlvExtractResult(null, directory.Combine(@"audio.aac")));
	}

	[Test]
	public async Task StreamWithOnlyASequenceHeaderCountsAsMissingAsync()
	{
		using TestDirectory directory = new();
		byte[] flv = new FlvBuilder()
			.Tag(FlvTagType.Video, 0, FlvSamples.AvcSequenceHeader)
			.Tag(FlvTagType.Audio, 0, FlvSamples.AacSequenceHeader)
			.Tag(FlvTagType.Video, 0, FlvSamples.AvcKeyFrame)
			.Tag(FlvTagType.Video, 33, FlvSamples.AvcKeyFrame)
			.ToArray();
		string input = directory.CreateFile(@"video.flv", flv);

		FlvExtractResult result = await FlvExtractor.ExtractAsync(input, directory.Root);

		await Assert.That(result.VideoPath).IsEqualTo(directory.Combine(@"video.h264"));
		await Assert.That(result.AudioPath).IsNull();
	}

	[Test]
	public async Task HeaderOnlyFlvYieldsNoStreamsAsync()
	{
		using TestDirectory directory = new();
		string input = directory.CreateFile(@"empty.flv", new FlvBuilder().ToArray());

		FlvExtractResult result = await FlvExtractor.ExtractAsync(input, directory.Root);

		await Assert.That(result).IsEqualTo(new FlvExtractResult(null, null));
	}

	[Test]
	public async Task CopiesTagsLargerThanTheInitialBufferAsync()
	{
		using TestDirectory directory = new();
		// 超过一开始租的 64 KiB 缓冲区
		byte[] largeSlice = [0x65, .. Enumerable.Range(0, 100_000).Select(static i => (byte)(i * 7 + 1))];
		byte[] largeFrame = [.. Convert.FromHexString(@"1701000000"), .. BigEndian(largeSlice.Length), .. largeSlice];
		byte[] flv = new FlvBuilder()
			.Tag(FlvTagType.Video, 0, FlvSamples.AvcSequenceHeader)
			.Tag(FlvTagType.Video, 0, FlvSamples.AvcKeyFrame)
			.Tag(FlvTagType.Video, 33, largeFrame)
			.Tag(FlvTagType.Video, 66, FlvSamples.AvcKeyFrame)
			.ToArray();
		string input = directory.CreateFile(@"large.flv", flv);

		FlvExtractResult result = await FlvExtractor.ExtractAsync(input, directory.Root);

		byte[] expected = FlvSamples.AnnexB(FlvSamples.Sps, FlvSamples.Pps, FlvSamples.IdrSlice, FlvSamples.Sei, largeSlice, FlvSamples.IdrSlice, FlvSamples.Sei);
		await Assert.That(result.VideoPath).IsNotNull();
		await Assert.That(Convert.ToHexString(await File.ReadAllBytesAsync(directory.Combine(@"large.h264")))).IsEqualTo(Convert.ToHexString(expected));

		static byte[] BigEndian(int value)
		{
			byte[] bytes = new byte[sizeof(int)];
			BinaryPrimitives.WriteInt32BigEndian(bytes, value);
			return bytes;
		}
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
