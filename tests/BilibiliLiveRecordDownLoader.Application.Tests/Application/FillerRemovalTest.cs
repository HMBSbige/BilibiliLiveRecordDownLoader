using BilibiliLiveRecordDownLoader.Application.Recording;
using BilibiliLiveRecordDownLoader.Application.Recording.Splitting;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using TUnit.Assertions.Enums;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class FillerRemovalTest
{
	private static readonly RecordingSplit OneMinute = new RecordingSplit.ByDuration(TimeSpan.FromMinutes(1));

	[Test]
	[Timeout(10_000)]
	[Arguments(FlvFillerStyle.Avc, 4)]
	[Arguments(FlvFillerStyle.Avc, 2)]
	[Arguments(FlvFillerStyle.LegacyHevc, 4)]
	[Arguments(FlvFillerStyle.EnhancedHevc, 4)]
	[Arguments(FlvFillerStyle.EnhancedAvcWithoutCompositionTime, 4)]
	public async Task FlvVideoTagsLoseTheirFillerNalUnitsAsync(FlvFillerStyle style, int lengthSize, CancellationToken cancellationToken)
	{
		FillerCodec codec = FillerSamples.GetCodec(style);
		byte[] Stream(bool removeFiller)
		{
			byte[] Frame(bool isKey, uint timestamp, byte[][] nals)
			{
				return FillerSamples.FlvFrame(style, isKey, timestamp, lengthSize, removeFiller ? FillerSamples.WithoutFiller(codec, nals) : nals);
			}

			return
			[
				.. FlvSamples.Header, .. FlvSamples.Metadata(), .. FillerSamples.FlvSequence(style, lengthSize), .. FlvSamples.AudioSequenceHeader(),
				.. Frame(true, 0, [FillerSamples.Picture(codec, true, "k1"), FillerSamples.Filler(codec, 40)]),
				.. FlvSamples.Audio(10, "a1"),
				.. Frame(false, 33, [FillerSamples.Filler(codec, 3), FillerSamples.Picture(codec, false, "p1"), FillerSamples.Lookalike(codec, "l1"), FillerSamples.Filler(codec, 200)]),
				.. Frame(false, 66, [FillerSamples.Filler(codec, 20)]),
				.. Frame(true, 100, [FillerSamples.Picture(codec, true, "k2")]),
				.. FlvSamples.Audio(110, "a2")
			];
		}

		byte[] stream = Stream(false);
		byte[] expected = Stream(true);
		SplitRecording recording = new();

		await recording.RunAsync(new FlvSplitter(), SplitRecording.Fixed(new RecordingSplit.None(), removeFiller: true), session => session.WriteInChunksAsync(stream, 7), cancellationToken);

		using (Assert.Multiple())
		{
			// 只剩填充的 tag 不动
			await Assert.That(recording.Files).IsEquivalentTo((byte[][])[expected], CollectionOrdering.Matching);
			await Assert.That(expected.Length).IsLessThan(stream.Length);
		}
	}

	// 多轨和 ModEx 里可能装着改了长度字段宽度的序列头
	[Test]
	[Timeout(10_000)]
	public async Task FlvFramesAfterAnUnrecognizedSequencePacketWaitForTheNextSequenceHeaderAsync(CancellationToken cancellationToken)
	{
		byte[][] nals = [FillerSamples.Picture(FillerCodec.Avc, false, "p"), FillerSamples.Filler(FillerCodec.Avc, 30)];
		byte[] modEx = FlvSamples.Tag(FlvSamples.VideoTag, 0, [0x80 | 0x10 | 7, 0, 0, 0x10, .. "avc1"u8, .. FillerSamples.DecoderConfiguration(FillerCodec.Avc, 2)]);
		byte[] Stream(bool removeFiller)
		{
			return
			[
				.. FlvSamples.Header, .. FillerSamples.FlvSequence(FlvFillerStyle.Avc, 4), .. modEx,
				.. FillerSamples.FlvFrame(FlvFillerStyle.Avc, false, 33, 4, nals),
				.. FillerSamples.FlvSequence(FlvFillerStyle.Avc, 4),
				.. FillerSamples.FlvFrame(FlvFillerStyle.Avc, false, 66, 4, removeFiller ? [nals[0]] : nals)
			];
		}

		SplitRecording recording = new();

		await recording.RunAsync(new FlvSplitter(), SplitRecording.Fixed(new RecordingSplit.None(), removeFiller: true), session => session.WriteAsync(Stream(false)), cancellationToken);

		await Assert.That(recording.Files).IsEquivalentTo((byte[][])[Stream(true)], CollectionOrdering.Matching);
	}

	[Test]
	[Timeout(10_000)]
	public async Task FlvTagLeftIncompleteAtTheEndIsDroppedAsync(CancellationToken cancellationToken)
	{
		byte[][] nals = [FillerSamples.Picture(FillerCodec.Avc, true, "k"), FillerSamples.Filler(FillerCodec.Avc, 60)];
		byte[] head = [.. FlvSamples.Header, .. FillerSamples.FlvSequence(FlvFillerStyle.Avc, 4)];
		byte[] last = FillerSamples.FlvFrame(FlvFillerStyle.Avc, false, 33, 4, nals)[..^10];
		SplitRecording recording = new();

		await recording.RunAsync(new FlvSplitter(), SplitRecording.Fixed(new RecordingSplit.None(), removeFiller: true), session => session.WriteAsync([.. head, .. FillerSamples.FlvFrame(FlvFillerStyle.Avc, true, 0, 4, nals), .. last]), cancellationToken);

		await Assert.That(recording.Files).IsEquivalentTo((byte[][])[[.. head, .. FillerSamples.FlvFrame(FlvFillerStyle.Avc, true, 0, 4, [nals[0]])]], CollectionOrdering.Matching);
	}

	[Test]
	[Timeout(10_000)]
	[Arguments(FlvTagCase.NalLengthsDoNotAddUp)]
	[Arguments(FlvTagCase.NoSequenceHeader)]
	[Arguments(FlvTagCase.TruncatedSequenceHeader)]
	[Arguments(FlvTagCase.MultitrackPacket)]
	[Arguments(FlvTagCase.UnknownFourCc)]
	public async Task FlvTagsThatCannotBeTakenApartAreWrittenUnchangedAsync(FlvTagCase tagCase, CancellationToken cancellationToken)
	{
		byte[][] nals = [FillerSamples.Picture(FillerCodec.Avc, true, "k1"), FillerSamples.Filler(FillerCodec.Avc, 40)];
		byte[] sample = FillerSamples.Sample(4, nals);
		byte[] sequence = tagCase switch
		{
			FlvTagCase.NoSequenceHeader => [],
			FlvTagCase.TruncatedSequenceHeader => FlvSamples.Tag(FlvSamples.VideoTag, 0, [0x17, 0, 0, 0, 0, 1, 0x64, 0, 0x1F]),
			_ => FillerSamples.FlvSequence(FlvFillerStyle.Avc, 4)
		};
		byte[] frame = tagCase switch
		{
			FlvTagCase.NalLengthsDoNotAddUp => FlvSamples.Tag(FlvSamples.VideoTag, 0, [0x17, 1, 0, 0, 0, .. sample, 0]),
			FlvTagCase.MultitrackPacket => FlvSamples.Tag(FlvSamples.VideoTag, 0, [0x80 | 0x10 | 6, 0x01, .. "avc1"u8, 0, .. sample]),
			FlvTagCase.UnknownFourCc => FlvSamples.Tag(FlvSamples.VideoTag, 0, [0x80 | 0x10 | 3, .. "vvc1"u8, .. sample]),
			_ => FillerSamples.FlvFrame(FlvFillerStyle.Avc, true, 0, 4, nals)
		};
		byte[] stream = [.. FlvSamples.Header, .. sequence, .. frame, .. FlvSamples.Audio(10, "a1")];
		SplitRecording recording = new();

		await recording.RunAsync(new FlvSplitter(), SplitRecording.Fixed(new RecordingSplit.None(), removeFiller: true), session => session.WriteAsync(stream), cancellationToken);

		await Assert.That(recording.Files).IsEquivalentTo((byte[][])[stream], CollectionOrdering.Matching);
	}

	// 这些序列头和帧在同一个攒着的单元里，提交时不能用已经看到后面序列头的状态
	[Test]
	[Timeout(10_000)]
	public async Task FlvSequenceHeadersAreFollowedInTheOrderTheyAreWrittenAsync(CancellationToken cancellationToken)
	{
		byte[] Stream(bool removeFiller)
		{
			byte[] Frame(bool isKey, uint timestamp, int lengthSize, string marker)
			{
				byte[][] nals = [FillerSamples.Picture(FillerCodec.Avc, isKey, marker), FillerSamples.Filler(FillerCodec.Avc, 30)];
				return FillerSamples.FlvFrame(FlvFillerStyle.Avc, isKey, timestamp, lengthSize, removeFiller ? FillerSamples.WithoutFiller(FillerCodec.Avc, nals) : nals);
			}

			return
			[
				.. FlvSamples.Header, .. FillerSamples.FlvSequence(FlvFillerStyle.Avc, 4),
				.. Frame(true, 0, 4, "k1"), .. Frame(false, 33, 4, "p1"),
				.. FillerSamples.FlvSequence(FlvFillerStyle.Avc, 2), .. Frame(false, 66, 2, "p2"),
				.. FillerSamples.FlvSequence(FlvFillerStyle.Avc, 4), .. Frame(true, 100, 4, "k2"), .. Frame(false, 133, 4, "p3")
			];
		}

		SplitRecording recording = new();

		await recording.RunAsync(new FlvSplitter(), SplitRecording.Fixed(OneMinute, removeFiller: true), session => session.WriteAsync(Stream(false)), cancellationToken);

		await Assert.That(recording.Files).IsEquivalentTo((byte[][])[Stream(true)], CollectionOrdering.Matching);
	}

	// 序列头在关闭时就过去了
	[Test]
	[Timeout(10_000)]
	public async Task TurningFillerRemovalOnTakesEffectFromTheNextWriteAsync(CancellationToken cancellationToken)
	{
		byte[][] nals = [FillerSamples.Picture(FillerCodec.Avc, false, "p"), FillerSamples.Filler(FillerCodec.Avc, 30)];
		byte[] head = [.. FlvSamples.Header, .. FillerSamples.FlvSequence(FlvFillerStyle.Avc, 4), .. FillerSamples.FlvFrame(FlvFillerStyle.Avc, false, 0, 4, nals)];
		bool removeFiller = false;
		SplitRecording recording = new();

		await recording.RunAsync(new FlvSplitter(), new SplitControl(static () => new RecordingSplitPolicy(new RecordingSplit.None(), false), () => removeFiller), async session =>
		{
			await session.WriteAsync(head);
			removeFiller = true;
			await session.WriteAsync(FillerSamples.FlvFrame(FlvFillerStyle.Avc, false, 33, 4, nals));
			removeFiller = false;
			await session.WriteAsync(FillerSamples.FlvFrame(FlvFillerStyle.Avc, false, 66, 4, nals));
		}, cancellationToken);

		await Assert.That(recording.Files).IsEquivalentTo((byte[][])[[.. head, .. FillerSamples.FlvFrame(FlvFillerStyle.Avc, false, 33, 4, [nals[0]]), .. FillerSamples.FlvFrame(FlvFillerStyle.Avc, false, 66, 4, nals)]], CollectionOrdering.Matching);
	}

	// 去掉的字节让写出的位置落后于收到的位置
	[Test]
	[Timeout(10_000)]
	public async Task FlvDataAfterAnUnrecognizedTagInALaterFileIsWrittenUnchangedAsync(CancellationToken cancellationToken)
	{
		byte[][] nals = [FillerSamples.Picture(FillerCodec.Avc, true, "k"), FillerSamples.Filler(FillerCodec.Avc, 100)];
		byte[] sequence = FillerSamples.FlvSequence(FlvFillerStyle.Avc, 4);
		byte[] unrecognized = FlvSamples.Tag(0x29, 10, [1, 2, 3]);
		byte[] lookalike = [FlvSamples.VideoTag, 0, 0, 0, 0xAA, 0xBB, 0xCC, 0xDD, 0, 0, 0, 1, 2, 3, 4];
		SplitRecording recording = new();

		await recording.RunAsync(new FlvSplitter(), SplitRecording.Fixed(OneMinute, removeFiller: true), async session =>
		{
			await session.WriteAsync([.. FlvSamples.Header, .. sequence, .. FillerSamples.FlvFrame(FlvFillerStyle.Avc, true, 0, 4, nals)]);
			session.Advance(TimeSpan.FromMinutes(1));
			await session.WriteAsync(FillerSamples.FlvFrame(FlvFillerStyle.Avc, true, 61_000, 4, nals));
			await session.WriteAsync(FillerSamples.FlvFrame(FlvFillerStyle.Avc, false, 61_033, 4, nals));
			await session.WriteAsync(unrecognized);
			await session.WriteAsync(lookalike);
		}, cancellationToken);

		byte[][] expected =
		[
			[.. FlvSamples.Header, .. sequence, .. FillerSamples.FlvFrame(FlvFillerStyle.Avc, true, 0, 4, [nals[0]])],
			[
				.. FlvSamples.Header, .. sequence,
				.. FillerSamples.FlvFrame(FlvFillerStyle.Avc, true, 0, 4, [nals[0]]), .. FillerSamples.FlvFrame(FlvFillerStyle.Avc, false, 33, 4, [nals[0]]),
				.. unrecognized, .. lookalike
			]
		];
		await Assert.That(recording.Files).IsEquivalentTo(expected, CollectionOrdering.Matching);
	}

	[Test]
	[Timeout(10_000)]
	[Arguments(FillerCodec.Avc, 4, Fmp4Layout.Bilibili)]
	[Arguments(FillerCodec.Hevc, 4, Fmp4Layout.Bilibili)]
	[Arguments(FillerCodec.Avc, 2, Fmp4Layout.LargeMdat)]
	[Arguments(FillerCodec.Avc, 4, Fmp4Layout.AudioFirst)]
	[Arguments(FillerCodec.Hevc, 4, Fmp4Layout.TwoVideoRuns)]
	public async Task Fmp4VideoSamplesLoseTheirFillerNalUnitsAsync(FillerCodec codec, int lengthSize, Fmp4Layout layout, CancellationToken cancellationToken)
	{
		byte[][][] firstVideo =
		[
			[FillerSamples.Picture(codec, true, "k1"), FillerSamples.Filler(codec, 50)],
			[FillerSamples.Filler(codec, 5), FillerSamples.Picture(codec, false, "p1"), FillerSamples.Lookalike(codec, "l1"), FillerSamples.Filler(codec, 300)],
			[FillerSamples.Filler(codec, 20)],
			[FillerSamples.Picture(codec, false, "p2")]
		];
		byte[][][] secondVideo = [[FillerSamples.Picture(codec, true, "k2"), FillerSamples.Filler(codec, 10)]];
		byte[][][] thirdVideo = [[FillerSamples.Picture(codec, false, "p3"), FillerSamples.Filler(codec, 15)], [FillerSamples.Filler(codec, 4), FillerSamples.Picture(codec, false, "p4")]];
		byte[][] audio = ["aac-1"u8.ToArray(), "aac-2"u8.ToArray()];
		byte[] init = FillerSamples.Init(codec, lengthSize);
		byte[] styp = Fmp4Samples.Box("styp", "msdh"u8.ToArray());

		byte[] Fragment(byte[][][] video, bool removeFiller)
		{
			return FillerSamples.Fragment([.. video.Select(nals => FillerSamples.Sample(lengthSize, removeFiller ? FillerSamples.WithoutFiller(codec, nals) : nals))], audio, layout);
		}

		SplitRecording recording = new();

		await recording.RunAsync(new Fmp4HlsSplitter(), SplitRecording.Fixed(new RecordingSplit.None(), removeFiller: true), async session =>
		{
			await session.InitAsync(init);
			await session.SegmentAsync([.. styp, .. Fragment(firstVideo, false)]);

			// 第二组 moof+mdat 处理时前面已经删掉过数据，要整体前移
			await session.SegmentAsync([.. Fragment(secondVideo, false), .. Fragment(thirdVideo, false)]);
		}, cancellationToken);

		await Assert.That(recording.Files).IsEquivalentTo((byte[][])[[.. init, .. styp, .. Fragment(firstVideo, true), .. Fragment(secondVideo, true), .. Fragment(thirdVideo, true)]], CollectionOrdering.Matching);
	}

	// 大小为 0 的样本和下一个样本起点相同；样本多于 16 个时排序不稳定，零长样本放在分区中点附近最容易被排到后面
	[Test]
	[Timeout(10_000)]
	[Arguments(13)]
	[Arguments(14)]
	public async Task Fmp4ZeroSizeVideoSampleKeepsItsPlaceAsync(int emptyIndex, CancellationToken cancellationToken)
	{
		byte[][] nals = [FillerSamples.Picture(FillerCodec.Avc, false, "p"), FillerSamples.Filler(FillerCodec.Avc, 30)];
		byte[] init = FillerSamples.Init(FillerCodec.Avc, 4);
		byte[] Fragment(bool removeFiller)
		{
			byte[] sample = FillerSamples.Sample(4, removeFiller ? [nals[0]] : nals);
			return FillerSamples.Fragment([.. Enumerable.Range(0, 30).Select(index => index == emptyIndex ? [] : sample)], ["aac"u8.ToArray()]);
		}

		SplitRecording recording = new();

		await recording.RunAsync(new Fmp4HlsSplitter(), SplitRecording.Fixed(new RecordingSplit.None(), removeFiller: true), async session =>
		{
			await session.InitAsync(init);
			await session.SegmentAsync(Fragment(false));
		}, cancellationToken);

		await Assert.That(recording.Files).IsEquivalentTo((byte[][])[[.. init, .. Fragment(true)]], CollectionOrdering.Matching);
	}

	// init 段在关闭时就过去了
	[Test]
	[Timeout(10_000)]
	public async Task TurningFillerRemovalOnTakesEffectFromTheNextFmp4FragmentAsync(CancellationToken cancellationToken)
	{
		byte[][] nals = [FillerSamples.Picture(FillerCodec.Avc, true, "k"), FillerSamples.Filler(FillerCodec.Avc, 30)];
		byte[] init = FillerSamples.Init(FillerCodec.Avc, 4);
		byte[] fragment = FillerSamples.Fragment([FillerSamples.Sample(4, nals)], ["aac"u8.ToArray()]);
		bool removeFiller = false;
		SplitRecording recording = new();

		await recording.RunAsync(new Fmp4HlsSplitter(), new SplitControl(static () => new RecordingSplitPolicy(new RecordingSplit.None(), false), () => removeFiller), async session =>
		{
			await session.InitAsync(init);
			await session.SegmentAsync(fragment);
			removeFiller = true;
			await session.SegmentAsync(fragment);
		}, cancellationToken);

		await Assert.That(recording.Files).IsEquivalentTo((byte[][])[[.. init, .. fragment, .. FillerSamples.Fragment([FillerSamples.Sample(4, [nals[0]])], ["aac"u8.ToArray()])]], CollectionOrdering.Matching);
	}

	[Test]
	[Timeout(10_000)]
	public async Task Fmp4FragmentLeftIncompleteAtTheEndIsDroppedAsync(CancellationToken cancellationToken)
	{
		byte[][] nals = [FillerSamples.Picture(FillerCodec.Avc, true, "k"), FillerSamples.Filler(FillerCodec.Avc, 60)];
		byte[] init = FillerSamples.Init(FillerCodec.Avc, 4);
		byte[] fragment = FillerSamples.Fragment([FillerSamples.Sample(4, nals)], ["aac"u8.ToArray()]);
		SplitRecording recording = new();

		await recording.RunAsync(new Fmp4HlsSplitter(), SplitRecording.Fixed(new RecordingSplit.None(), removeFiller: true), async session =>
		{
			await session.InitAsync(init);
			await session.SegmentAsync(fragment);
			await session.PartialSegmentAsync(fragment, fragment.Length - 10);
		}, cancellationToken);

		await Assert.That(recording.Files).IsEquivalentTo((byte[][])[[.. init, .. FillerSamples.Fragment([FillerSamples.Sample(4, [nals[0]])], ["aac"u8.ToArray()])]], CollectionOrdering.Matching);
	}

	// 后面跟一个能处理的分片，确认原样写的分片不影响后面的解析
	[Test]
	[Timeout(10_000)]
	[MethodDataSource(nameof(UnchangedFmp4Fragments))]
	public async Task Fmp4FragmentsThatCannotBeTakenApartAreWrittenUnchangedAsync(byte[] init, byte[] fragment, bool isNextProcessed, CancellationToken cancellationToken)
	{
		byte[][] nals = [FillerSamples.Picture(FillerCodec.Avc, true, "k2"), FillerSamples.Filler(FillerCodec.Avc, 50)];
		byte[] next = FillerSamples.Fragment([FillerSamples.Sample(4, nals)], ["aac"u8.ToArray()]);
		byte[] processedNext = isNextProcessed ? FillerSamples.Fragment([FillerSamples.Sample(4, [nals[0]])], ["aac"u8.ToArray()]) : next;
		SplitRecording recording = new();

		await recording.RunAsync(new Fmp4HlsSplitter(), SplitRecording.Fixed(new RecordingSplit.None(), removeFiller: true), async session =>
		{
			await session.InitAsync(init);
			await session.SegmentAsync(fragment);
			await session.SegmentAsync(next);
		}, cancellationToken);

		await Assert.That(recording.Files).IsEquivalentTo((byte[][])[[.. init, .. fragment, .. processedNext]], CollectionOrdering.Matching);
	}

	public static IEnumerable<TestDataRow<Func<(byte[] Init, byte[] Fragment, bool IsNextProcessed)>>> UnchangedFmp4Fragments()
	{
		// sidx 记着分片的字节数，遇到之后这一场都不再去
		yield return new(static () => (FillerSamples.Init(FillerCodec.Avc, 4), [.. Fmp4Samples.Box("sidx", new byte[24]), .. FillerSamples.Fragment([Sample()], ["aac"u8.ToArray()])], false), DisplayName: "带 sidx");

		foreach (Fmp4Layout layout in (Fmp4Layout[])[Fmp4Layout.NotBasedOnMoof, Fmp4Layout.BaseDataOffset, Fmp4Layout.VideoDefaultSampleSize, Fmp4Layout.Encrypted, Fmp4Layout.MdatSizeZero, Fmp4Layout.DataOutsideMdat, Fmp4Layout.OverlappingRuns, Fmp4Layout.HugeAudioSampleCount])
		{
			yield return new(() => (FillerSamples.Init(FillerCodec.Avc, 4), FillerSamples.Fragment([Sample(), Sample()], ["aac"u8.ToArray()], layout), true), DisplayName: $"分片布局 {layout}");
		}

		// 一个样本对不上时整个分片都不动
		yield return new(static () => (FillerSamples.Init(FillerCodec.Avc, 4), FillerSamples.Fragment([Sample(), [.. Sample(), 0]], ["aac"u8.ToArray()]), true), DisplayName: "NAL 长度加起来和样本大小对不上");
		yield return new(static () => (FillerSamples.Init(FillerCodec.Avc, 4, "encv"), FillerSamples.Fragment([Sample()], ["aac"u8.ToArray()]), false), DisplayName: "加密的样本描述");

		static byte[] Sample()
		{
			return FillerSamples.Sample(4, [FillerSamples.Picture(FillerCodec.Avc, true, "k1"), FillerSamples.Filler(FillerCodec.Avc, 50)]);
		}
	}

	[Test]
	[Timeout(10_000)]
	public async Task DisabledFillerRemovalKeepsEveryByteAsync(CancellationToken cancellationToken)
	{
		byte[][] nals = [FillerSamples.Picture(FillerCodec.Avc, true, "k"), FillerSamples.Filler(FillerCodec.Avc, 100)];
		byte[] flv = [.. FlvSamples.Header, .. FillerSamples.FlvSequence(FlvFillerStyle.Avc, 4), .. FillerSamples.FlvFrame(FlvFillerStyle.Avc, true, 0, 4, nals), .. FillerSamples.FlvFrame(FlvFillerStyle.Avc, true, 100, 4, nals)];
		byte[] init = FillerSamples.Init(FillerCodec.Avc, 4);
		byte[] fragment = FillerSamples.Fragment([FillerSamples.Sample(4, nals)], ["aac"u8.ToArray()]);
		SplitRecording flvRecording = new();
		SplitRecording fmp4Recording = new();

		await flvRecording.RunAsync(new FlvSplitter(), SplitRecording.Fixed(OneMinute), session => session.WriteInChunksAsync(flv, 9), cancellationToken);
		await fmp4Recording.RunAsync(new Fmp4HlsSplitter(), SplitRecording.Fixed(OneMinute), async session =>
		{
			await session.InitAsync(init);
			await session.SegmentAsync(fragment);
			await session.SegmentAsync(fragment);
		}, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(flvRecording.Files).IsEquivalentTo((byte[][])[flv], CollectionOrdering.Matching);
			await Assert.That(fmp4Recording.Files).IsEquivalentTo((byte[][])[[.. init, .. fragment, .. fragment]], CollectionOrdering.Matching);
		}
	}

	[Test]
	[Arguments(true, StreamCodec.Avc, FillerRemoval.Avc)]
	[Arguments(true, StreamCodec.Hevc, FillerRemoval.Hevc)]
	[Arguments(true, StreamCodec.Av1, FillerRemoval.None)]
	[Arguments(false, StreamCodec.Avc, FillerRemoval.None)]
	public async Task FFmpegRecordingRemovesFillerByTheSettingAndCodecAsync(bool removeFiller, StreamCodec codec, FillerRemoval expected)
	{
		AppConfig config = AppConfig.CreateDefault();
		config.RemoveFillerData = removeFiller;

		await Assert.That(LiveRecorder.GetFillerRemoval(config, codec)).IsEqualTo(expected);
	}
}

public enum FlvTagCase
{
	NalLengthsDoNotAddUp,
	NoSequenceHeader,
	TruncatedSequenceHeader,
	MultitrackPacket,
	UnknownFourCc
}
