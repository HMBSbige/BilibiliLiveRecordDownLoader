using BilibiliLiveRecordDownLoader.Application.Recording;
using BilibiliLiveRecordDownLoader.Application.Recording.Splitting;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;

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
		SplitRecording recording = new();

		await recording.RunAsync(new FlvSplitter(), SplitRecording.Fixed(new RecordingSplit.None(), removeFiller: true), session => session.WriteInChunksAsync(stream, 7), cancellationToken);

		using (Assert.Multiple())
		{
			// 期望的文件按去掉填充的 NAL 重新拼出，DataSize 和 PreviousTagSize 都重新计算；只剩填充的 tag 不动
			await Assert.That(recording.Files.Single().SequenceEqual(Stream(true))).IsTrue();
			await Assert.That(Stream(true).Length).IsLessThan(stream.Length);
		}
	}

	// 多轨和 ModEx 里可能装着改了长度字段宽度的序列头，之后的帧要等下一个认得出的序列头才去
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

		await Assert.That(recording.Files.Single().SequenceEqual(Stream(true))).IsTrue();
	}

	// 收流结束时末尾不完整的 tag 原样写，前面完整的照常去
	[Test]
	[Timeout(10_000)]
	public async Task FlvTagLeftIncompleteAtTheEndIsWrittenUnchangedAsync(CancellationToken cancellationToken)
	{
		byte[][] nals = [FillerSamples.Picture(FillerCodec.Avc, true, "k"), FillerSamples.Filler(FillerCodec.Avc, 60)];
		byte[] head = [.. FlvSamples.Header, .. FillerSamples.FlvSequence(FlvFillerStyle.Avc, 4)];
		byte[] last = FillerSamples.FlvFrame(FlvFillerStyle.Avc, false, 33, 4, nals)[..^10];
		SplitRecording recording = new();

		await recording.RunAsync(new FlvSplitter(), SplitRecording.Fixed(new RecordingSplit.None(), removeFiller: true), session => session.WriteAsync([.. head, .. FillerSamples.FlvFrame(FlvFillerStyle.Avc, true, 0, 4, nals), .. last]), cancellationToken);

		byte[] expected = [.. head, .. FillerSamples.FlvFrame(FlvFillerStyle.Avc, true, 0, 4, [nals[0]]), .. last];
		await Assert.That(recording.Files.Single().SequenceEqual(expected)).IsTrue();
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

		await Assert.That(recording.Files.Single().SequenceEqual(stream)).IsTrue();
	}

	// 两个序列头和用到它们的帧在同一个攒着的单元里：提交时要按写出的顺序换长度字段宽度，不能用已经看到后面序列头的状态
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

		await Assert.That(recording.Files.Single().SequenceEqual(Stream(true))).IsTrue();
	}

	// 序列头在关闭时就过去了，中途打开之后的帧照样能去
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

		byte[] expected = [.. head, .. FillerSamples.FlvFrame(FlvFillerStyle.Avc, false, 33, 4, [nals[0]]), .. FillerSamples.FlvFrame(FlvFillerStyle.Avc, false, 66, 4, nals)];
		await Assert.That(recording.Files.Single().SequenceEqual(expected)).IsTrue();
	}

	// 去掉的字节让写出的位置落后于收到的位置，放弃解析之后的数据仍然不能按 tag 改时间戳
	[Test]
	[Timeout(10_000)]
	public async Task FlvDataAfterAnUnrecognizedTagInALaterFileIsWrittenUnchangedAsync(CancellationToken cancellationToken)
	{
		byte[][] nals = [FillerSamples.Picture(FillerCodec.Avc, true, "k"), FillerSamples.Filler(FillerCodec.Avc, 100)];
		byte[] sequence = FillerSamples.FlvSequence(FlvFillerStyle.Avc, 4);
		byte[] unrecognized = FlvSamples.Tag(0x29, 10, [1, 2, 3]);

		// 看起来像视频 tag 的头，时间戳字节是 AA BB CC DD
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

		byte[] expected =
		[
			.. FlvSamples.Header, .. sequence,
			.. FillerSamples.FlvFrame(FlvFillerStyle.Avc, true, 0, 4, [nals[0]]), .. FillerSamples.FlvFrame(FlvFillerStyle.Avc, false, 33, 4, [nals[0]]),
			.. unrecognized, .. lookalike
		];
		await Assert.That(recording.Files[1].SequenceEqual(expected)).IsTrue();
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

			// 一个分片里有两组 moof+mdat，第二组处理时前面已经删掉过数据，要整体前移
			await session.SegmentAsync([.. Fragment(secondVideo, false), .. Fragment(thirdVideo, false)]);
		}, cancellationToken);

		// 期望的分片按去掉填充的样本重新拼出：视频样本大小、各 trun 的 data_offset 和 mdat 大小都重新计算
		byte[] expected = [.. init, .. styp, .. Fragment(firstVideo, true), .. Fragment(secondVideo, true), .. Fragment(thirdVideo, true)];
		await Assert.That(recording.Files.Single().SequenceEqual(expected)).IsTrue();
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

		await Assert.That(recording.Files.Single().SequenceEqual((byte[])[.. init, .. Fragment(true)])).IsTrue();
	}

	// init 段在关闭时就过去了，中途打开之后的分片照样能去
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

		byte[] expected = [.. init, .. fragment, .. FillerSamples.Fragment([FillerSamples.Sample(4, [nals[0]])], ["aac"u8.ToArray()])];
		await Assert.That(recording.Files.Single().SequenceEqual(expected)).IsTrue();
	}

	// 收流结束时末尾不完整的分片原样写
	[Test]
	[Timeout(10_000)]
	public async Task Fmp4FragmentLeftIncompleteAtTheEndIsWrittenUnchangedAsync(CancellationToken cancellationToken)
	{
		byte[][] nals = [FillerSamples.Picture(FillerCodec.Avc, true, "k"), FillerSamples.Filler(FillerCodec.Avc, 60)];
		byte[] init = FillerSamples.Init(FillerCodec.Avc, 4);
		byte[] fragment = FillerSamples.Fragment([FillerSamples.Sample(4, nals)], ["aac"u8.ToArray()]);
		byte[] last = fragment[..^10];
		SplitRecording recording = new();

		await recording.RunAsync(new Fmp4HlsSplitter(), SplitRecording.Fixed(new RecordingSplit.None(), removeFiller: true), async session =>
		{
			await session.InitAsync(init);
			await session.SegmentAsync(fragment);
			await session.WriteAsync(last);
		}, cancellationToken);

		byte[] expected = [.. init, .. FillerSamples.Fragment([FillerSamples.Sample(4, [nals[0]])], ["aac"u8.ToArray()]), .. last];
		await Assert.That(recording.Files.Single().SequenceEqual(expected)).IsTrue();
	}

	// sidx 记着分片的字节数，遇到之后这一场都不再去
	[Test]
	[Timeout(10_000)]
	public async Task Fmp4WithASegmentIndexIsWrittenUnchangedAsync(CancellationToken cancellationToken)
	{
		byte[] sample = FillerSamples.Sample(4, [FillerSamples.Picture(FillerCodec.Avc, true, "k1"), FillerSamples.Filler(FillerCodec.Avc, 50)]);
		byte[] fragment = FillerSamples.Fragment([sample], ["aac"u8.ToArray()]);

		await AssertFmp4UnchangedAsync(FillerSamples.Init(FillerCodec.Avc, 4), [.. Fmp4Samples.Box("sidx", new byte[24]), .. fragment], false, cancellationToken);
	}

	[Test]
	[Timeout(10_000)]
	[Arguments(Fmp4Layout.NotBasedOnMoof)]
	[Arguments(Fmp4Layout.BaseDataOffset)]
	[Arguments(Fmp4Layout.VideoDefaultSampleSize)]
	[Arguments(Fmp4Layout.Encrypted)]
	[Arguments(Fmp4Layout.MdatSizeZero)]
	[Arguments(Fmp4Layout.DataOutsideMdat)]
	[Arguments(Fmp4Layout.OverlappingRuns)]
	[Arguments(Fmp4Layout.HugeAudioSampleCount)]
	public async Task Fmp4FragmentsWhoseDataCannotBeLocatedAreWrittenUnchangedAsync(Fmp4Layout layout, CancellationToken cancellationToken)
	{
		byte[] sample = FillerSamples.Sample(4, [FillerSamples.Picture(FillerCodec.Avc, true, "k1"), FillerSamples.Filler(FillerCodec.Avc, 50)]);
		byte[] init = FillerSamples.Init(FillerCodec.Avc, 4);
		byte[] fragment = FillerSamples.Fragment([sample, sample], ["aac"u8.ToArray()], layout);

		await AssertFmp4UnchangedAsync(init, fragment, true, cancellationToken);
	}

	[Test]
	[Timeout(10_000)]
	public async Task Fmp4SamplesWhoseNalLengthsDoNotAddUpAreWrittenUnchangedAsync(CancellationToken cancellationToken)
	{
		byte[] good = FillerSamples.Sample(4, [FillerSamples.Picture(FillerCodec.Avc, true, "k1"), FillerSamples.Filler(FillerCodec.Avc, 50)]);
		byte[] init = FillerSamples.Init(FillerCodec.Avc, 4);

		// 一个样本对不上时整个分片都不动
		await AssertFmp4UnchangedAsync(init, FillerSamples.Fragment([good, [.. good, 0]], ["aac"u8.ToArray()]), true, cancellationToken);
	}

	[Test]
	[Timeout(10_000)]
	public async Task Fmp4WithAnEncryptedSampleEntryIsWrittenUnchangedAsync(CancellationToken cancellationToken)
	{
		byte[] sample = FillerSamples.Sample(4, [FillerSamples.Picture(FillerCodec.Avc, true, "k1"), FillerSamples.Filler(FillerCodec.Avc, 50)]);

		await AssertFmp4UnchangedAsync(FillerSamples.Init(FillerCodec.Avc, 4, "encv"), FillerSamples.Fragment([sample], ["aac"u8.ToArray()]), false, cancellationToken);
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
			await Assert.That(flvRecording.Files.Single().SequenceEqual(flv)).IsTrue();
			await Assert.That(fmp4Recording.Files.Single().SequenceEqual((byte[])[.. init, .. fragment, .. fragment])).IsTrue();
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

	// 后面跟一个能处理的分片，确认原样写的分片不影响后面的解析；样本项加密时拿不到长度字段宽度，后面的也不动
	private static async Task AssertFmp4UnchangedAsync(byte[] init, byte[] fragment, bool isNextProcessed, CancellationToken cancellationToken)
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

		await Assert.That(recording.Files.Single().SequenceEqual((byte[])[.. init, .. fragment, .. processedNext])).IsTrue();
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
