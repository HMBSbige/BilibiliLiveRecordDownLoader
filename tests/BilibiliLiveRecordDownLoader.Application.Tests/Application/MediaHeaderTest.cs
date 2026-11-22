using BilibiliLiveRecordDownLoader.Application.Recording;
using BilibiliLiveRecordDownLoader.Application.Recording.Splitting;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using System.Buffers.Binary;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

// 从流头读画面尺寸和元素的媒体时间
public sealed class MediaHeaderTest
{
	// x264 和 x265 对 testsrc 编码得到的 SPS，ffprobe 给出的尺寸写在后面；1080 和 768 要靠裁剪，854 宽要裁掉补齐的宏块，都带防竞争字节
	internal const string Avc1080 = "67640028acd940780227e5c044000003000400000300f03c60c658";
	internal const string Avc480 = "674d401feca06c1ef3780880000003008000001e078c18cb";
	internal const string Avc444720 = "67f4001f919b280a00b76022000003000200000300781e30632c";
	internal const string Hevc768 = "420101016000000300900000030000030078a002ac80301d796566924caf016808000003000800000300f040";
	internal const string Hevc1080 = "420101016000000300900000030000030078a003c08010e596566924caf0168080000003008000000f04";

	[Test]
	[Arguments(Avc1080, false, 1920, 1080)]
	[Arguments(Avc480, false, 854, 480)]
	[Arguments(Avc444720, false, 1280, 720)]
	[Arguments(Hevc768, true, 1366, 768)]
	[Arguments(Hevc1080, true, 1920, 1080)]
	public async Task SpsGivesTheDisplaySizeAsync(string sps, bool isHevc, int width, int height)
	{
		byte[] nal = Convert.FromHexString(sps);
		VideoResolution expected = new(width, height);

		using (Assert.Multiple())
		{
			await Assert.That(isHevc ? ParameterSets.ReadHevcSps(nal) : ParameterSets.ReadAvcSps(nal)).IsEqualTo(expected);
			await Assert.That(ParameterSets.ReadAnnexB([0, 0, 0, 1, 0x09, 0xF0, 0, 0, 1, .. nal, 0, 0, 1, 0x68, 0xEB], isHevc ? NalSyntax.Hevc : NalSyntax.Avc)).IsEqualTo(expected);
		}
	}

	[Test]
	public async Task TruncatedSpsIsNotReadAsync()
	{
		byte[] nal = Convert.FromHexString(Avc1080);

		await Assert.That(ParameterSets.ReadAnnexB([0, 0, 1, .. nal.AsSpan(0, 8)], NalSyntax.Avc)).IsNull();
	}

	[Test]
	public async Task FlvPrefersTheSpsOverOnMetaDataAsync()
	{
		FlvSplitter splitter = new();
		byte[] stream = [.. FlvSamples.Header, .. FlvSamples.MetadataWithSize(640, 360), .. FlvSamples.AvcSequenceHeader(Convert.FromHexString(Avc1080))];

		Scan(splitter, stream);

		await Assert.That(splitter.Resolution).IsEqualTo(new VideoResolution(1920, 1080));
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task FlvReadsHevcSequenceHeadersAsync(bool isEnhanced)
	{
		FlvSplitter splitter = new();

		Scan(splitter, [.. FlvSamples.Header, .. FlvSamples.HevcSequenceHeader(Convert.FromHexString(Hevc768), isEnhanced)]);

		await Assert.That(splitter.Resolution).IsEqualTo(new VideoResolution(1366, 768));
	}

	// AV1 的序列头不解析，只能用编码器写的尺寸
	[Test]
	public async Task FlvFallsBackToOnMetaDataAsync()
	{
		FlvSplitter splitter = new();

		Scan(splitter, [.. FlvSamples.Header, .. FlvSamples.MetadataWithSize(1280, 720), .. FlvSamples.Video(VideoStyle.EnhancedAv1, VideoPacket.SequenceHeader, 0, "vseq")]);

		await Assert.That(splitter.Resolution).IsEqualTo(new VideoResolution(1280, 720));
	}

	[Test]
	public async Task FlvReportsTimestampsOfAudioAndVideoFramesOnlyAsync()
	{
		FlvSplitter splitter = new();
		byte[] header = [.. FlvSamples.Header, .. FlvSamples.Metadata(5), .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.SequenceHeader, 0, "vseq"), .. FlvSamples.AudioSequenceHeader()];
		byte[] key = FlvSamples.Video(VideoStyle.Avc, VideoPacket.KeyFrame, 100_000, "k");
		byte[] audio = FlvSamples.Audio(99_990);

		Scan(splitter, [.. header, .. key, .. audio]);

		await Assert.That(splitter.MediaMarks.ToArray().Select(static mark => (mark.End, mark.Span)).SequenceEqual([((long)header.Length + key.Length, MediaSpan.At(TimeSpan.FromMilliseconds(100_000))), ((long)header.Length + key.Length + audio.Length, MediaSpan.At(TimeSpan.FromMilliseconds(99_990)))])).IsTrue();
	}

	[Test]
	[Arguments((byte)0)]
	[Arguments((byte)1)]
	public async Task Fmp4InitGivesTheTimescaleAndSampleEntrySizeAsync(byte mdhdVersion)
	{
		Fmp4HlsSplitter splitter = new();
		splitter.OnInitSegment(Fmp4Samples.TimedInit(90_000, 1920, 1080, mdhdVersion: mdhdVersion));

		await Assert.That(splitter.Resolution).IsEqualTo(new VideoResolution(1920, 1080));
		await Assert.That(IsoBmff.TryReadMovie(Fmp4Samples.TimedInit(90_000, 1920, 1080, 3000, mdhdVersion), out Fmp4VideoTrack? video) ? video : null).IsEqualTo(new Fmp4VideoTrack(Fmp4Samples.VideoTrack, 3000, Fmp4Samples.NonSyncFlags, 90_000, new VideoResolution(1920, 1080)));
	}

	// 一个分片覆盖第一个样本的解码时间到最后一个样本结束；样本时长可以逐个给出，也可以用 trex 的默认值
	[Test]
	[Arguments(3000u, 0u, (byte)1)]
	[Arguments(0u, 3000u, (byte)0)]
	public async Task Fmp4FragmentSpansItsSamplesAsync(uint sampleDuration, uint defaultDuration, byte tfdtVersion)
	{
		Fmp4VideoTrack track = new(Fmp4Samples.VideoTrack, defaultDuration, 0, 90_000, null);

		await Assert.That(IsoBmff.ReadFragmentTime(Fmp4Samples.TimedFragment(900_000, 30, sampleDuration, tfdtVersion: tfdtVersion), track)).IsEqualTo((900_000UL, 990_000UL));
	}

	[Test]
	public async Task TsSegmentSpansItsVideoPtsAndGivesTheSpsSizeAsync()
	{
		TsHlsSplitter splitter = new();
		byte[] segment = [.. TsSamples.Pat(), .. TsSamples.Pmt(TsSamples.HevcStreamType), .. TsSamples.TimedVideoFrame(TsSamples.HevcIdr, 900_000, true, Convert.FromHexString(Hevc1080), true), .. TsSamples.Audio(), .. TsSamples.TimedVideoFrame(TsSamples.HevcTrail, 990_000, isHevc: true), .. TsSamples.TimedVideoFrame(TsSamples.HevcTrail, 945_000, isHevc: true)];

		splitter.OnMediaSegment(segment);
		Scan(splitter, segment);

		using (Assert.Multiple())
		{
			await Assert.That(splitter.Resolution).IsEqualTo(new VideoResolution(1920, 1080));
			await Assert.That(splitter.MediaMarks.ToArray().Single().Span).IsEqualTo(new MediaSpan(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(11)));
		}
	}

	[Test]
	public async Task FFmpegTransportStreamReportsEachVideoPesAndTheSpsSizeAsync()
	{
		TransportStreamSplitter splitter = new();
		byte[] stream = [.. TsSamples.Pat(), .. TsSamples.Pmt(), .. TsSamples.TimedVideoFrame(TsSamples.AvcIdr, 126_000, true, Convert.FromHexString(Avc480)), .. TsSamples.Audio(), .. TsSamples.TimedVideoFrame(TsSamples.AvcSlice, 129_000)];

		Scan(splitter, stream);

		using (Assert.Multiple())
		{
			await Assert.That(splitter.Resolution).IsEqualTo(new VideoResolution(854, 480));
			await Assert.That(splitter.MediaMarks.ToArray().Select(static mark => mark.Span.Start).SequenceEqual([TimeSpan.FromMilliseconds(1400), TimeSpan.FromTicks(129_000L * TimeSpan.TicksPerSecond / 90_000)])).IsTrue();
		}
	}

	// 关键帧自带的 SPS 换了尺寸时，这一帧的时间带的是新尺寸；从它开始的新段按这一帧定尺寸
	[Test]
	public async Task FFmpegTransportStreamMarksAKeyFrameWithTheSizeOfItsOwnSpsAsync()
	{
		TransportStreamSplitter splitter = new();
		byte[] stream = [.. TsSamples.Pat(), .. TsSamples.Pmt(), .. TsSamples.TimedVideoFrame(TsSamples.AvcIdr, 126_000, true, Convert.FromHexString(Avc480)), .. TsSamples.TimedVideoFrame(TsSamples.AvcSlice, 129_000), .. TsSamples.TimedVideoFrame(TsSamples.AvcIdr, 132_000, true, Convert.FromHexString(Avc1080))];

		Scan(splitter, stream);

		await Assert.That(splitter.MediaMarks.ToArray().Select(static mark => mark.Resolution).SequenceEqual([new VideoResolution(854, 480), new VideoResolution(854, 480), new VideoResolution(1920, 1080)])).IsTrue();
	}

	// 损坏的文件里 mdhd 可以是空盒子，读不到 timescale 也照样给出样本项里的尺寸
	[Test]
	public async Task EmptyMdhdLeavesOnlyTheTimescaleUnreadAsync()
	{
		byte[] tkhd = new byte[24];
		BinaryPrimitives.WriteUInt32BigEndian(tkhd.AsSpan(12), Fmp4Samples.VideoTrack);
		byte[] hdlr = new byte[24];
		"vide"u8.CopyTo(hdlr.AsSpan(8));
		byte[] entry = new byte[78];
		BinaryPrimitives.WriteUInt16BigEndian(entry.AsSpan(24), 1280);
		BinaryPrimitives.WriteUInt16BigEndian(entry.AsSpan(26), 720);
		byte[] stsd = [0, 0, 0, 0, 0, 0, 0, 1, .. Fmp4Samples.Box("avc1", entry)];
		byte[] trak = Fmp4Samples.Box("trak", [.. Fmp4Samples.Box("tkhd", tkhd), .. Fmp4Samples.Box("mdia", [.. Fmp4Samples.Box("mdhd", []), .. Fmp4Samples.Box("hdlr", hdlr), .. Fmp4Samples.Box("minf", Fmp4Samples.Box("stbl", Fmp4Samples.Box("stsd", stsd)))])]);

		bool hasMovie = IsoBmff.TryReadMovie(Fmp4Samples.Box("moov", trak), out Fmp4VideoTrack? video);

		using (Assert.Multiple())
		{
			await Assert.That(hasMovie).IsTrue();
			await Assert.That(video).IsEqualTo(new Fmp4VideoTrack(Fmp4Samples.VideoTrack, 0, 0, null, new VideoResolution(1280, 720)));
		}
	}

	[Test]
	public async Task EmptyTfdtGivesNoFragmentTimeAsync()
	{
		byte[] tfhd = new byte[8];
		BinaryPrimitives.WriteUInt32BigEndian(tfhd.AsSpan(4), Fmp4Samples.VideoTrack);
		byte[] fragment = Fmp4Samples.Box("moof", Fmp4Samples.Box("traf", [.. Fmp4Samples.Box("tfhd", tfhd), .. Fmp4Samples.Box("tfdt", [])]));

		await Assert.That(IsoBmff.ReadFragmentTime(fragment, new Fmp4VideoTrack(Fmp4Samples.VideoTrack, 3000, 0, 90_000, null))).IsNull();
	}

	[Test]
	public async Task PtsKeepsCountingAcrossTheWrapAsync()
	{
		const long period = 1L << 33;
		PtsClock clock = new();
		TimeSpan[] times = [clock.Unwrap(period - 90_000), clock.Unwrap(0), clock.Unwrap(90_000), clock.Unwrap(period - 45_000), clock.Unwrap(180_000)];
		TimeSpan start = times[0];

		await Assert.That(times.Select(time => time - start).SequenceEqual([TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(3)])).IsTrue();
	}

	// 只让分割器看完所有完整的元素，不写文件
	private static void Scan(MediaSplitter splitter, byte[] data)
	{
		int position = 0;

		while (splitter.TryFindSplit(data, ref position))
		{
		}
	}
}
