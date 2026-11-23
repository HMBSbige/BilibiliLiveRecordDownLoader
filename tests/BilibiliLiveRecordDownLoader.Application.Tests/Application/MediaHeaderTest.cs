using BilibiliLiveRecordDownLoader.Application.Recording;
using BilibiliLiveRecordDownLoader.Application.Recording.Splitting;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using System.Buffers.Binary;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class MediaHeaderTest
{
	// x264 和 x265 对 testsrc 编码得到的 SPS，尺寸来自 ffprobe；1080、768 和 854 宽都要靠裁剪，都带防竞争字节
	// HevcSubLayers766 带 3 个时间子层，x265 写的子层存在标志都是 0，只有补齐位要跳过；HevcSubLayerInfo766 是在它上面手工补上第 0 层的档次和级别、第 1 层的级别，ffprobe 读出的尺寸不变
	internal const string Avc1080 = "67640028acd940780227e5c044000003000400000300f03c60c658";
	internal const string Avc480 = "674d401feca06c1ef3780880000003008000001e078c18cb";
	internal const string Avc444720 = "67f4001f919b280a00b76022000003000200000300781e30632c";
	internal const string Hevc768 = "420101016000000300900000030000030078a002ac80301d796566924caf016808000003000800000300f040";
	internal const string Hevc1080 = "420101016000000300900000030000030078a003c08010e596566924caf0168080000003008000000f04";
	private const string HevcSubLayers766 = "4201040408000003009e080000030000780000900055900603bbcb2b29594acb24995e02dc08080010000003001000000301e080";
	private const string HevcSubLayerInfo766 = "4201040408000003009e08000003000078d0000408000003009e0800000300007878900055900603bbcb2b29594acb24995e02dc08080010000003001000000301e080";

	[Test]
	[Arguments(Avc1080, false, 1920, 1080)]
	[Arguments(Avc480, false, 854, 480)]
	[Arguments(Avc444720, false, 1280, 720)]
	[Arguments(Hevc768, true, 1366, 768)]
	[Arguments(Hevc1080, true, 1920, 1080)]
	[Arguments(HevcSubLayers766, true, 1366, 766)]
	[Arguments(HevcSubLayerInfo766, true, 1366, 766)]
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

		await Assert.That(splitter.MediaMarks.ToArray().Select(static mark => (mark.End, mark.Span))).IsEquivalentTo([((long)header.Length + key.Length, MediaSpan.At(TimeSpan.FromMilliseconds(100_000))), ((long)header.Length + key.Length + audio.Length, MediaSpan.At(TimeSpan.FromMilliseconds(99_990)))], CollectionOrdering.Matching);
	}

	// 没有 mdhd 的音频轨不列出
	[Test]
	[Arguments((byte)0)]
	[Arguments((byte)1)]
	public async Task Fmp4InitGivesEachTrackTimescaleAndTheSampleEntrySizeAsync(byte mdhdVersion)
	{
		byte[] init = Fmp4Samples.TimedInit(90_000, 1920, 1080, 3000, mdhdVersion);
		Fmp4HlsSplitter splitter = new();
		splitter.OnInitSegment(init);

		using (Assert.Multiple())
		{
			await Assert.That(splitter.Resolution).IsEqualTo(new VideoResolution(1920, 1080));
			await Assert.That(ReadTracks(init)).IsEquivalentTo([new Fmp4Track(Fmp4Samples.VideoTrack, 90_000, 3000)], CollectionOrdering.Matching);
			await Assert.That(IsoBmff.TryReadMovie(init, out Fmp4VideoTrack? video) ? video : null).IsEqualTo(new Fmp4VideoTrack(Fmp4Samples.VideoTrack, Fmp4Samples.NonSyncFlags, new VideoResolution(1920, 1080)));
		}
	}

	[Test]
	[Arguments(3000u, 0u, (byte)1)]
	[Arguments(0u, 3000u, (byte)0)]
	public async Task Fmp4FragmentSpansItsSamplesAsync(uint sampleDuration, uint defaultDuration, byte tfdtVersion)
	{
		Fmp4TrackTimes times = new([new Fmp4Track(Fmp4Samples.VideoTrack, 90_000, defaultDuration)]);

		await Assert.That(times.ReadFragmentSpans(Fmp4Samples.TimedFragment(900_000, 30, sampleDuration, tfdtVersion: tfdtVersion))).IsEquivalentTo([new MediaSpan(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(11))], CollectionOrdering.Matching);
	}

	// 往回的小差值和差得很远的（源重新开始）都不加周期，交给 SegmentClock 判断
	[Test]
	public async Task Fmp4Version0DecodeTimeAddsAPeriodOnlyWhenItWrapsAsync()
	{
		const ulong period = 1UL << 32;
		Fmp4Track track = new(Fmp4Samples.VideoTrack, 90_000, 0);
		Fmp4TrackTimes times = new([track]);
		ulong[] values = [period - 180_000, 90_000, 45_000, 3_000_000_000, 1_000];
		ulong[] expected = [period - 180_000, period + 90_000, period + 45_000, period + 3_000_000_000, period + 1_000];

		MediaSpan[] spans = [.. values.SelectMany(value => times.ReadFragmentSpans(Fmp4Samples.TimedFragment(value, 1, 0, tfdtVersion: 0)))];

		await Assert.That(spans.Select(static span => span.Start.Ticks)).IsEquivalentTo(expected.Select(track.ToTicks), CollectionOrdering.Matching);
	}

	// 按分片里的顺序，第一个 PES 不是最早的
	[Test]
	public async Task TsSegmentReportsEveryTimedPesByDecodeTimeAndGivesTheSpsSizeAsync()
	{
		TsHlsSplitter splitter = new();
		byte[] segment = [.. TsSamples.Pat(), .. TsSamples.Pmt(TsSamples.HevcStreamType), .. TsSamples.TimedVideoFrame(TsSamples.HevcIdr, 907_200, true, Convert.FromHexString(Hevc1080), true, 900_000), .. TsSamples.TimedPes(TsSamples.AudioPid, 896_400), .. TsSamples.TimedVideoFrame(TsSamples.HevcTrail, 990_000, isHevc: true), .. TsSamples.TimedPes(TsSamples.AudioPid, 993_600)];

		splitter.OnMediaSegment(segment);
		Scan(splitter, segment);

		using (Assert.Multiple())
		{
			await Assert.That(splitter.Resolution).IsEqualTo(new VideoResolution(1920, 1080));
			await Assert.That(splitter.MediaMarks.ToArray().Select(static mark => mark.Span)).IsEquivalentTo([MediaSpan.At(TimeSpan.FromSeconds(10)), MediaSpan.At(TimeSpan.FromMilliseconds(9960)), MediaSpan.At(TimeSpan.FromSeconds(11)), MediaSpan.At(TimeSpan.FromMilliseconds(11_040))], CollectionOrdering.Matching);
		}
	}

	[Test]
	public async Task FFmpegTransportStreamReportsEveryTimedPesByDecodeTimeAndTheSpsSizeAsync()
	{
		TransportStreamSplitter splitter = new();
		byte[] stream = [.. TsSamples.Pat(), .. TsSamples.Pmt(), .. TsSamples.TimedVideoFrame(TsSamples.AvcIdr, 133_200, true, Convert.FromHexString(Avc480), dts: 126_000), .. TsSamples.TimedPes(TsSamples.AudioPid, 124_200), .. TsSamples.TimedVideoFrame(TsSamples.AvcSlice, 129_600)];

		Scan(splitter, stream);

		using (Assert.Multiple())
		{
			await Assert.That(splitter.Resolution).IsEqualTo(new VideoResolution(854, 480));
			await Assert.That(splitter.MediaMarks.ToArray().Select(static mark => mark.Span)).IsEquivalentTo([MediaSpan.At(TimeSpan.FromMilliseconds(1400)), MediaSpan.At(TimeSpan.FromMilliseconds(1380)), MediaSpan.At(TimeSpan.FromMilliseconds(1440))], CollectionOrdering.Matching);
		}
	}

	// 从这一帧开始的新段按它定尺寸
	[Test]
	public async Task FFmpegTransportStreamMarksAKeyFrameWithTheSizeOfItsOwnSpsAsync()
	{
		TransportStreamSplitter splitter = new();
		byte[] stream = [.. TsSamples.Pat(), .. TsSamples.Pmt(), .. TsSamples.TimedVideoFrame(TsSamples.AvcIdr, 126_000, true, Convert.FromHexString(Avc480)), .. TsSamples.TimedVideoFrame(TsSamples.AvcSlice, 129_000), .. TsSamples.TimedVideoFrame(TsSamples.AvcIdr, 132_000, true, Convert.FromHexString(Avc1080))];

		Scan(splitter, stream);

		await Assert.That(splitter.MediaMarks.ToArray().Select(static mark => mark.Resolution)).IsEquivalentTo((VideoResolution?[])[new VideoResolution(854, 480), new VideoResolution(854, 480), new VideoResolution(1920, 1080)], CollectionOrdering.Matching);
	}

	// 损坏的文件里 mdhd 可以是空盒子
	[Test]
	public async Task EmptyMdhdLeavesTheTrackOutButKeepsTheSampleEntrySizeAsync()
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

		byte[] init = Fmp4Samples.Box("moov", trak);
		bool hasMovie = IsoBmff.TryReadMovie(init, out Fmp4VideoTrack? video);

		using (Assert.Multiple())
		{
			await Assert.That(hasMovie).IsTrue();
			await Assert.That(video).IsEqualTo(new Fmp4VideoTrack(Fmp4Samples.VideoTrack, 0, new VideoResolution(1280, 720)));
			await Assert.That(ReadTracks(init)).IsEmpty();
		}
	}

	[Test]
	public async Task EmptyTfdtGivesNoFragmentTimeAsync()
	{
		byte[] tfhd = new byte[8];
		BinaryPrimitives.WriteUInt32BigEndian(tfhd.AsSpan(4), Fmp4Samples.VideoTrack);
		byte[] fragment = Fmp4Samples.Box("moof", Fmp4Samples.Box("traf", [.. Fmp4Samples.Box("tfhd", tfhd), .. Fmp4Samples.Box("tfdt", [])]));

		await Assert.That(new Fmp4TrackTimes([new Fmp4Track(Fmp4Samples.VideoTrack, 90_000, 3000)]).ReadFragmentSpans(fragment)).IsEmpty();
	}

	[Test]
	public async Task PtsKeepsCountingAcrossTheWrapAsync()
	{
		const long period = 1L << 33;
		PtsClock clock = new();
		TimeSpan[] times = [clock.Unwrap(period - 90_000), clock.Unwrap(0), clock.Unwrap(90_000), clock.Unwrap(period - 45_000), clock.Unwrap(180_000)];
		TimeSpan start = times[0];

		await Assert.That(times.Select(time => time - start)).IsEquivalentTo([TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(3)], CollectionOrdering.Matching);
	}

	private static Fmp4Track[] ReadTracks(byte[] init)
	{
		return IsoBmff.TryFindBox(init, "moov"u8, out ReadOnlySpan<byte> moov) ? IsoBmff.ReadTracks(moov) : [];
	}

	private static void Scan(MediaSplitter splitter, byte[] data)
	{
		int position = 0;

		while (splitter.TryFindSplit(data, ref position))
		{
		}
	}
}
