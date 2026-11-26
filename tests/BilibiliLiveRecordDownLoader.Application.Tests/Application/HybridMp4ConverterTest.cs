using BilibiliLiveRecordDownLoader.Application.Recording.Hybrid;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using Microsoft.Win32.SafeHandles;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class HybridMp4ConverterTest
{
	// 写死而不引用 HybridMovie.Signature：标记已经写进转换过的文件，改了就认不出它们
	private static readonly byte[] Signature = [0x0C, 0x91, 0x11, 0x39, 0xB1, 0x9F, 0x47, 0x79, 0x83, 0x3A, 0xCC, 0xB1, 0x43, 0xBE, 0xE1, 0x05];

	private static readonly MediaChapter[] Chapters =
	[
		new(TimeSpan.Zero, TimeSpan.FromMilliseconds(60_500), "测试标题"),
		new(TimeSpan.FromMilliseconds(60_500), TimeSpan.FromMilliseconds(150_123.9), "高能：哈哈哈"),
		new(TimeSpan.FromMilliseconds(150_123.9), TimeSpan.FromSeconds(200), "最后")
	];

	[Test]
	public async Task EveryRecordedByteStaysInPlaceBehindTheNewHeaderAsync()
	{
		using TestDirectory directory = new();
		byte[] original = HybridSamples.Recording();
		string path = directory.WriteAllBytes(HybridSamples.FileName, original);

		HybridConversionResult result = HybridMp4Converter.Convert(path, []);

		byte[] converted = await File.ReadAllBytesAsync(path);
		int movieStart = HybridSamples.FileType().Length;
		int dataStart = movieStart + 16;
		TopBox[] boxes = Mp4Inspector.ReadTopLevel(converted);

		using (Assert.Multiple())
		{
			await Assert.That(result).IsTypeOf<HybridConversionResult.Converted>();
			await Assert.That(boxes.Select(static box => box.Type)).IsEquivalentTo(["ftyp", "mdat", "moov"], CollectionOrdering.Matching);
			await Assert.That(boxes[1]).IsEqualTo(new TopBox("mdat", movieStart, original.Length - movieStart));
			await Assert.That(converted[dataStart..original.Length]).IsEquivalentTo(original[dataStart..], CollectionOrdering.Matching);
		}

		await AssertSamplesAsync(converted, Mp4Inspector.ReadTracks(converted));
	}

	// 原来的 dash 品牌换掉，按视频编码列出兼容品牌，长度和原来的 ftyp 相同
	[Test]
	[Arguments("avc1", "isomiso2avc1mp41")]
	[Arguments("av01", "isomav01iso2mp41")]
	[Arguments("hvc1", "isomiso2mp41isom")]
	public async Task FileTypeListsTheBrandsOfTheConvertedFileAsync(string codec, string compatibleBrands)
	{
		using TestDirectory directory = new();
		string path = directory.WriteAllBytes(HybridSamples.FileName, [.. HybridSamples.Init(codec), .. HybridSamples.Fragment(1, new RunSpec(HybridSamples.VideoTrack, 0, HybridSamples.Video(10)), new RunSpec(HybridSamples.AudioTrack, 0, HybridSamples.Audio(10)))]);

		HybridMp4Converter.Convert(path, []);

		await Assert.That(Encoding.ASCII.GetString(await File.ReadAllBytesAsync(path), 0, 32)).IsEqualTo("\0\0\0 ftypisom\0\0\u0002\0" + compatibleBrands);
	}

	[Test]
	public async Task ConvertingAgainChangesNothingAsync()
	{
		using TestDirectory directory = new();
		string path = directory.WriteAllBytes(HybridSamples.FileName, HybridSamples.Recording());
		HybridMp4Converter.Convert(path, Chapters);
		byte[] first = await File.ReadAllBytesAsync(path);

		HybridConversionResult result = HybridMp4Converter.Convert(path, Chapters);

		using (Assert.Multiple())
		{
			await Assert.That(result).IsTypeOf<HybridConversionResult.AlreadyPlain>();
			await Assert.That(await File.ReadAllBytesAsync(path)).IsEquivalentTo(first, CollectionOrdering.Matching);
		}
	}

	[Test]
	public async Task HeaderBackupRestoresTheRecordedFileAsync()
	{
		using TestDirectory directory = new();
		byte[] original = HybridSamples.Recording();
		string path = directory.WriteAllBytes(HybridSamples.FileName, original);
		HybridMp4Converter.Convert(path, Chapters);
		byte[] converted = await File.ReadAllBytesAsync(path);

		byte[] backup = Mp4Inspector.Children(Mp4Inspector.Get(Mp4Inspector.ReadMovie(converted), "udta"), "uuid").Single();
		int headerLength = HybridSamples.FileType().Length + 16;
		long originalLength = (long)BinaryPrimitives.ReadUInt64BigEndian(backup.AsSpan(16));
		byte[] header = backup.AsSpan(24).ToArray();
		byte[] restored = [.. header, .. converted.AsSpan(header.Length, (int)originalLength - header.Length)];

		using (Assert.Multiple())
		{
			await Assert.That(backup.AsSpan(0, 16).ToArray()).IsEquivalentTo(Signature, CollectionOrdering.Matching);
			await Assert.That(header).IsEquivalentTo(original.AsSpan(0, headerLength).ToArray(), CollectionOrdering.Matching);
			await Assert.That(restored).IsEquivalentTo(original, CollectionOrdering.Matching);
		}
	}

	// 每一步之后进程被杀时录下的字节都没动，文件仍是原来的分片 MP4，再转换一次的结果和一口气转完的逐字节相同
	// 没有章节时尾巴不以带标记的章节 mdat 开头，要靠 free 里的 mvhd 或翻好的 moov 认出来
	// 录制中途写失败时最后一个盒子不完整，追加的盒子不在盒子边界上，只能靠新 moov 末尾的头部备份认出来；追加只写了一半时备份也没了，认不出来
	[Test]
	[MatrixDataSource]
	[MatrixExclusion(Truncation.InsideLastMediaData, Interruption.TornAppend, false)]
	[MatrixExclusion(Truncation.InsideLastMediaData, Interruption.TornAppend, true)]
	[MatrixExclusion(Truncation.InsideLastMovieFragment, Interruption.TornAppend, false)]
	[MatrixExclusion(Truncation.InsideLastMovieFragment, Interruption.TornAppend, true)]
	public async Task InterruptedConversionIsFinishedByTheNextOneAsync([Matrix] Truncation truncation, [Matrix] Interruption interruption, [Matrix] bool hasChapters)
	{
		using TestDirectory directory = new();
		MediaChapter[] chapters = hasChapters ? Chapters : [];
		byte[] original = Truncate(HybridSamples.Recording(), truncation);
		string expectedPath = directory.WriteAllBytes("expected_fmp4.mp4", original);
		HybridMp4Converter.Convert(expectedPath, chapters);
		string path = directory.WriteAllBytes(HybridSamples.FileName, original);

		using (SafeFileHandle handle = File.OpenHandle(path, FileMode.Open, FileAccess.ReadWrite))
		{
			HybridMp4Converter converter = Prepare(handle, chapters);
			converter.Truncate();
			converter.Append();

			if (interruption is Interruption.TornAppend)
			{
				RandomAccess.SetLength(handle, original.Length + (RandomAccess.GetLength(handle) - original.Length) / 2);
			}
			else if (interruption is Interruption.AfterFlip)
			{
				await Assert.That(converter.Verify()).IsNull();
				converter.Flip();
			}
		}

		byte[] interrupted = await File.ReadAllBytesAsync(path);
		FragmentedFile stillFragmented = ReadLayout(path);

		HybridConversionResult result = HybridMp4Converter.Convert(path, chapters);

		using (Assert.Multiple())
		{
			await Assert.That(interrupted[..original.Length]).IsEquivalentTo(original, CollectionOrdering.Matching);
			await Assert.That(stillFragmented).IsTypeOf<FragmentedFile.Fragmented>().And.Member(static file => file.Layout.DataEnd, end => end.IsEqualTo(original.Length));
			await Assert.That(result).IsTypeOf<HybridConversionResult.Converted>();
			await Assert.That(await File.ReadAllBytesAsync(path)).IsEquivalentTo(await File.ReadAllBytesAsync(expectedPath), CollectionOrdering.Matching);
		}
	}

	[Test]
	public async Task MovieThatFailsVerificationLeavesTheRecordingAsItWasAsync()
	{
		using TestDirectory directory = new();

		// data_offset 指向 moof 自己，读的时候还在文件里，但不在任何 mdat 里
		byte[] original = [.. HybridSamples.Init(hasAudio: false), .. HybridSamples.Fragment(1, new RunSpec(HybridSamples.VideoTrack, 0, HybridSamples.Video(10), DataOffset: 8)), .. HybridSamples.Fragment(2, new RunSpec(HybridSamples.VideoTrack, 30_000, HybridSamples.Video(10, firstIndex: 10)))];
		string path = directory.WriteAllBytes(HybridSamples.FileName, original);

		HybridConversionResult result = HybridMp4Converter.Convert(path, []);

		using (Assert.Multiple())
		{
			await Assert.That(result).IsTypeOf<HybridConversionResult.Rejected>().And.Member(static rejected => rejected.Reason, static reason => reason.Contains("outside the media data", StringComparison.Ordinal));
			await Assert.That(await File.ReadAllBytesAsync(path)).IsEquivalentTo(original, CollectionOrdering.Matching);
		}
	}

	// 新 moov 写到磁盘后改掉其中一个字段，校验读回来才发现
	[Test]
	[Arguments(Corruption.ChunkOffsetOnTheFileType, "outside the media data")]
	[Arguments(Corruption.MediaSampleDuration, "instead of the mdhd duration")]
	[Arguments(Corruption.ChapterSampleDuration, "instead of the mdhd duration")]
	public async Task VerificationReadsTheWrittenMovieBackAsync(Corruption corruption, string reason)
	{
		using TestDirectory directory = new();
		byte[] original = HybridSamples.Recording();
		string path = directory.WriteAllBytes(HybridSamples.FileName, original);
		using SafeFileHandle handle = File.OpenHandle(path, FileMode.Open, FileAccess.ReadWrite);
		HybridMp4Converter converter = Prepare(handle, Chapters);
		converter.Truncate();
		converter.Append();
		await Assert.That(converter.Verify()).IsNull();

		byte[] appended = new byte[RandomAccess.GetLength(handle) - original.Length];
		RandomAccess.Read(handle, appended, original.Length);

		// 类型之后是版本和标志、表项数，然后是第一个表项：stco 的块偏移，stts 的样本数和时长；第一条轨道是视频，章节轨写在最后
		(int field, uint value) = corruption switch
		{
			Corruption.ChunkOffsetOnTheFileType => (appended.AsSpan().IndexOf("stco"u8) + 12, 0u),
			Corruption.MediaSampleDuration => Lengthen(appended.AsSpan().IndexOf("stts"u8) + 16),
			Corruption.ChapterSampleDuration => Lengthen(appended.AsSpan().LastIndexOf("stts"u8) + 16),
			_ => throw new UnreachableException()
		};
		RandomAccess.Write(handle, Fmp4Samples.Words(value), original.Length + field);

		await Assert.That(converter.Verify()).Contains(reason, StringComparison.Ordinal);

		// 样本数不变，样本数之和照样对得上
		(int, uint) Lengthen(int offset)
		{
			return (offset, BinaryPrimitives.ReadUInt32BigEndian(appended.AsSpan(offset)) + 1);
		}
	}

	// 一个块只有 0 字节的样本，又从某个 mdat 数据的结尾开始：块的范围是空的，仍在那个 mdat 里
	[Test]
	public async Task EmptyChunkAtTheEndOfTheMediaDataIsInsideItAsync()
	{
		using TestDirectory directory = new();
		string path = directory.WriteAllBytes(HybridSamples.FileName,
		[
			.. HybridSamples.Init(),
			.. HybridSamples.Fragment(1, new RunSpec(HybridSamples.VideoTrack, 0, HybridSamples.Video(10)), new RunSpec(HybridSamples.AudioTrack, 0, HybridSamples.Audio(10))),
			.. HybridSamples.Fragment(2, new RunSpec(HybridSamples.VideoTrack, 30_000, HybridSamples.Video(10, firstIndex: 10)), new RunSpec(HybridSamples.AudioTrack, 10_240, [new SampleSpec(1024, 0), new SampleSpec(1024, 0)]))
		]);

		HybridConversionResult result = HybridMp4Converter.Convert(path, []);

		using (Assert.Multiple())
		{
			await Assert.That(result).IsTypeOf<HybridConversionResult.Converted>();
			await Assert.That(Mp4Inspector.ReadTracks(await File.ReadAllBytesAsync(path))[1].Samples.Length).IsEqualTo(12);
		}
	}

	// 第 4 步一次改写到 moov 头为止，只有在一个扇区里才不会写一半：moov 从 32 字节的 ftyp 和 8 + 456 字节的 free 之后开始，加上 16 字节的 mdat 头正好 512
	[Test]
	public async Task HeadEndingAtTheSectorBoundaryIsConvertedAsync()
	{
		using TestDirectory directory = new();
		string path = directory.WriteAllBytes(HybridSamples.FileName, RecordingAfterFree(456));

		HybridConversionResult result = HybridMp4Converter.Convert(path, []);

		byte[] converted = await File.ReadAllBytesAsync(path);
		await Assert.That(result).IsTypeOf<HybridConversionResult.Converted>();
		await AssertSamplesAsync(converted, Mp4Inspector.ReadTracks(converted));
	}

	// 多一个字节就超出一个扇区，ftyp 和 moov 之间隔着大盒子时不转换
	[Test]
	public async Task HeadPastTheSectorIsRejectedAsync()
	{
		using TestDirectory directory = new();
		byte[] original = RecordingAfterFree(457);
		string path = directory.WriteAllBytes(HybridSamples.FileName, original);

		HybridConversionResult result = HybridMp4Converter.Convert(path, []);

		using (Assert.Multiple())
		{
			await Assert.That(result).IsTypeOf<HybridConversionResult.Rejected>().And.Member(static rejected => rejected.Reason, static reason => reason.Contains("one sector", StringComparison.Ordinal));
			await Assert.That(await File.ReadAllBytesAsync(path)).IsEquivalentTo(original, CollectionOrdering.Matching);
		}
	}

	// 第二个分片的音频和视频都和第一个重叠两个样本：解码时间逐个改成上一个样本的加 1，之后的样本不变
	// 视频的合成偏移整体加 3000 才不是负数；推后的第一帧原来显示在 6000，早于新的解码时间 9001 减 3000，改成显示在 6001；第二帧仍显示在原来的 15000
	[Test]
	public async Task SamplesThatGoBackInTimeFollowThePreviousOneByOneTickAsync()
	{
		using TestDirectory directory = new();
		SampleSpec[] video = [new(3000, 900), new(3000, 900, false, 6000), new(3000, 900, false, -3000), new(3000, 900, false, -3000)];
		string path = directory.WriteAllBytes(HybridSamples.FileName,
		[
			.. HybridSamples.Init(),
			.. HybridSamples.Fragment(1, new RunSpec(HybridSamples.VideoTrack, 0, video), new RunSpec(HybridSamples.AudioTrack, 0, HybridSamples.Audio(4))),
			.. HybridSamples.Fragment(2, new RunSpec(HybridSamples.VideoTrack, 6000, video), new RunSpec(HybridSamples.AudioTrack, 2048, HybridSamples.Audio(4)))
		]);

		HybridConversionResult result = HybridMp4Converter.Convert(path, []);

		byte[] converted = await File.ReadAllBytesAsync(path);
		TrackTable[] tracks = Mp4Inspector.ReadTracks(converted);

		using (Assert.Multiple())
		{
			await Assert.That(result).IsTypeOf<HybridConversionResult.Converted>().And.Member(static converted => converted.DroppedSamples, static dropped => dropped.IsEqualTo(0)).And.Member(static converted => converted.RetimedSamples, static retimed => retimed.IsEqualTo(4));

			// 视频解码时间 0、3000、6000、9000、9001、9002、12000、15000
			await Assert.That(tracks[0].TimeToSample).IsEquivalentTo([(3u, 3000u), (2u, 1u), (1u, 2998u), (2u, 3000u)], CollectionOrdering.Matching);
			await Assert.That(tracks[0].CompositionOffsets).IsEquivalentTo([(1u, 3000), (1u, 9000), (3u, 0), (1u, 8998), (2u, 0)], CollectionOrdering.Matching);

			// 推后的第二帧显示到 18000，是最晚的；媒体起点加上 3000，第一帧仍显示在 0
			await Assert.That(tracks[0].Edits).IsEquivalentTo([new EditListEntry(18_000 * 8, 3000)], CollectionOrdering.Matching);

			// 音频解码时间 0、1024、2048、3072、3073、3074、4096、5120
			await Assert.That(tracks[1].TimeToSample).IsEquivalentTo([(3u, 1024u), (2u, 1u), (1u, 1022u), (2u, 1024u)], CollectionOrdering.Matching);
			await Assert.That(tracks[1].CompositionOffsets).IsEmpty();
		}

		await AssertSamplesAsync(converted, tracks);
	}

	// 媒体时长超过 int.MaxValue 时 mdhd 写版本 1，90 kHz 的视频录到约 6.6 小时就会这样
	[Test]
	[Arguments(90_000u, (byte)0)]
	[Arguments((uint)int.MaxValue, (byte)1)]
	public async Task GapStretchesTheSampleBeforeItAsync(uint gap, byte mediaHeaderVersion)
	{
		using TestDirectory directory = new();
		string path = directory.WriteAllBytes(HybridSamples.FileName,
		[
			.. HybridSamples.Init(hasAudio: false),
			.. HybridSamples.Fragment(1, new RunSpec(HybridSamples.VideoTrack, 0, HybridSamples.Video(10))),
			.. HybridSamples.Fragment(2, new RunSpec(HybridSamples.VideoTrack, 30_000 + gap, HybridSamples.Video(10, firstIndex: 10)))
		]);

		HybridConversionResult result = HybridMp4Converter.Convert(path, []);

		byte[] converted = await File.ReadAllBytesAsync(path);
		TrackTable video = Mp4Inspector.ReadTracks(converted).Single();

		using (Assert.Multiple())
		{
			await Assert.That(result).IsTypeOf<HybridConversionResult.Converted>();
			await Assert.That(video.TimeToSample).IsEquivalentTo([(9u, 3000u), (1u, 3000u + gap), (10u, 3000u)], CollectionOrdering.Matching);
			await Assert.That(video.MediaDuration).IsEqualTo(60_000L + gap);
			await Assert.That(Mp4Inspector.Get(Mp4Inspector.ReadMovie(converted), "trak", "mdia", "mdhd")[0]).IsEqualTo(mediaHeaderVersion);
		}
	}

	// 视频前三帧在第一个关键帧之前，缺参考帧，丢掉；音频没有关键帧之分，全部保留
	// 音频从 4800 / 48000 秒开始，早于留下的第一帧（12000 / 90000 秒），电影从音频开始，丢掉的帧不影响起点；视频用空编辑推迟 3000 / 90000 秒
	[Test]
	public async Task VideoBeforeTheFirstKeyFrameIsDroppedButAudioIsKeptAsync()
	{
		using TestDirectory directory = new();
		SampleSpec[] video = [.. HybridSamples.Video(13, keyFrameInterval: 10, firstIndex: 7)];
		string path = directory.WriteAllBytes(HybridSamples.FileName,
		[
			.. HybridSamples.Init(),
			.. HybridSamples.Fragment(1, new RunSpec(HybridSamples.VideoTrack, 0, video), new RunSpec(HybridSamples.AudioTrack, 4800, HybridSamples.Audio(20)))
		]);

		HybridConversionResult result = HybridMp4Converter.Convert(path, []);

		TrackTable[] tracks = Mp4Inspector.ReadTracks(await File.ReadAllBytesAsync(path));

		using (Assert.Multiple())
		{
			await Assert.That(result).IsTypeOf<HybridConversionResult.Converted>().And.Member(static converted => converted.DroppedSamples, static dropped => dropped.IsEqualTo(3));
			await Assert.That(tracks[0].Samples.Length).IsEqualTo(10);
			await Assert.That(tracks[0].SyncSamples).IsEquivalentTo([1u], CollectionOrdering.Matching);
			await Assert.That(tracks[1].Samples.Length).IsEqualTo(20);
			await Assert.That(tracks[1].SyncSamples).IsNull();

			// 留下的帧显示在 12000 到 42000，第一帧的合成偏移是 3000；电影时间刻度 720000，每个视频单位是 8，每个音频单位是 15
			await Assert.That(tracks[0].Edits).IsEquivalentTo([new EditListEntry(3000 * 8, -1), new EditListEntry(30_000 * 8, 3000)], CollectionOrdering.Matching);
			await Assert.That(tracks[1].Edits).IsEquivalentTo([new EditListEntry(20 * 1024 * 15, 0)], CollectionOrdering.Matching);
		}
	}

	// 音频从 96 / 48000 秒开始，视频第一帧解码在 0、显示在 6000 / 90000 秒：电影从音频开始，视频用空编辑推迟 6000 - 180 个视频单位，媒体起点是第一帧的合成偏移
	// 电影时间刻度是两个轨道时间刻度的最小公倍数 720000，每个视频单位是 8，每个音频单位是 15
	[Test]
	public async Task LaterTrackIsDelayedByAnEmptyEditAsync()
	{
		using TestDirectory directory = new();
		SampleSpec[] video = [new(3000, 900, true, 6000), new(3000, 900, false, 12_000), new(3000, 900, false, 3000), new(3000, 900, false, 3000)];
		string path = directory.WriteAllBytes(HybridSamples.FileName,
		[
			.. HybridSamples.Init(),
			.. HybridSamples.Fragment(1, new RunSpec(HybridSamples.VideoTrack, 0, video), new RunSpec(HybridSamples.AudioTrack, 96, HybridSamples.Audio(6)))
		]);

		HybridMp4Converter.Convert(path, []);

		byte[] converted = await File.ReadAllBytesAsync(path);
		TrackTable[] tracks = Mp4Inspector.ReadTracks(converted);

		using (Assert.Multiple())
		{
			// 视频显示在 6000 到 3000 + 12000 + 3000 = 18000
			await Assert.That(tracks[0].Edits).IsEquivalentTo([new EditListEntry(5820 * 8, -1), new EditListEntry(12_000 * 8, 6000)], CollectionOrdering.Matching);
			await Assert.That(tracks[0].Duration).IsEqualTo((5820 + 12_000) * 8);
			await Assert.That(tracks[0].CompositionOffsets).IsEquivalentTo([(1u, 6000), (1u, 12_000), (2u, 3000)], CollectionOrdering.Matching);
			await Assert.That(tracks[0].MediaDuration).IsEqualTo(12_000);
			await Assert.That(tracks[1].Edits).IsEquivalentTo([new EditListEntry(6 * 1024 * 15, 0)], CollectionOrdering.Matching);
			await Assert.That(tracks[1].Duration).IsEqualTo(6 * 1024 * 15);
			await Assert.That(Mp4Inspector.ReadMovieHeader(converted)).IsEqualTo((720_000u, (5820L + 12_000) * 8));
		}
	}

	// 第一帧解码和显示都在 0，后面 B 帧的合成偏移是负数，音频也从 0 开始
	// 合成偏移整体加 3000 写成非负数，编辑列表的媒体起点也是 3000，第一帧仍和音频同时显示
	[Test]
	public async Task NegativeCompositionOffsetsKeepTheirPresentationTimesAsync()
	{
		using TestDirectory directory = new();
		SampleSpec[] video = [new(3000, 900), new(3000, 900, false, 6000), new(3000, 900, false, -3000), new(3000, 900, false, -3000)];
		string path = directory.WriteAllBytes(HybridSamples.FileName,
		[
			.. HybridSamples.Init(),
			.. HybridSamples.Fragment(1, new RunSpec(HybridSamples.VideoTrack, 0, video), new RunSpec(HybridSamples.AudioTrack, 0, HybridSamples.Audio(6)))
		]);

		HybridMp4Converter.Convert(path, []);

		TrackTable[] tracks = Mp4Inspector.ReadTracks(await File.ReadAllBytesAsync(path));

		using (Assert.Multiple())
		{
			await Assert.That(tracks[0].CompositionOffsets).IsEquivalentTo([(1u, 3000), (1u, 9000), (2u, 0)], CollectionOrdering.Matching);

			// 视频显示在 0 到 9000 + 3000
			await Assert.That(tracks[0].Edits).IsEquivalentTo([new EditListEntry(12_000 * 8, 3000)], CollectionOrdering.Matching);
			await Assert.That(tracks[1].Edits).IsEquivalentTo([new EditListEntry(6 * 1024 * 15, 0)], CollectionOrdering.Matching);
		}
	}

	// init 段的编辑列表从媒体时间 1024 开始显示音频，前 1024 个采样是编码延迟；转出来的编辑列表仍然跳过它们，音频剩下的部分和视频同时开始
	[Test]
	public async Task EncoderDelayHiddenByTheInitEditListStaysHiddenAsync()
	{
		using TestDirectory directory = new();
		SampleSpec[] video = [new(3000, 900), new(3000, 900, false), new(3000, 900, false)];
		string path = directory.WriteAllBytes(HybridSamples.FileName,
		[
			.. HybridSamples.Init(audioEditList: HybridSamples.EditList(1024)),
			.. HybridSamples.Fragment(1, new RunSpec(HybridSamples.VideoTrack, 0, video), new RunSpec(HybridSamples.AudioTrack, 0, HybridSamples.Audio(8)))
		]);

		HybridMp4Converter.Convert(path, []);

		TrackTable[] tracks = Mp4Inspector.ReadTracks(await File.ReadAllBytesAsync(path));

		using (Assert.Multiple())
		{
			await Assert.That(tracks[0].Edits).IsEquivalentTo([new EditListEntry(9000 * 8, 0)], CollectionOrdering.Matching);
			await Assert.That(tracks[1].Edits).IsEquivalentTo([new EditListEntry(7 * 1024 * 15, 1024)], CollectionOrdering.Matching);
		}
	}

	// AAC 每帧的输出要和前一帧重叠相加，拖动后要先解前一帧：音频轨的每个样本都属于 roll_distance 为 -1 的 roll 样本组
	[Test]
	public async Task AacSamplesAreMarkedToDecodeTheFrameBeforeAsync()
	{
		using TestDirectory directory = new();
		string path = directory.WriteAllBytes(HybridSamples.FileName, HybridSamples.Recording());

		HybridMp4Converter.Convert(path, []);

		byte[][] tracks = [.. Mp4Inspector.Children(Mp4Inspector.ReadMovie(await File.ReadAllBytesAsync(path)), "trak")];
		byte[] audio = Mp4Inspector.Get(tracks[1], "mdia", "minf", "stbl");
		byte[] sgpd = Mp4Inspector.Get(audio, "sgpd");
		byte[] sbgp = Mp4Inspector.Get(audio, "sbgp");

		using (Assert.Multiple())
		{
			// 版本 1，之后依次是分组类型、默认项长度、项数和 roll_distance
			await Assert.That((sgpd[0], Encoding.ASCII.GetString(sgpd, 4, 4), BinaryPrimitives.ReadUInt32BigEndian(sgpd.AsSpan(12)), BinaryPrimitives.ReadInt16BigEndian(sgpd.AsSpan(16)))).IsEqualTo(((byte)1, "roll", 1u, (short)-1));

			// 一个表项把全部 136 个样本归到第 1 组
			await Assert.That((Encoding.ASCII.GetString(sbgp, 4, 4), BinaryPrimitives.ReadUInt32BigEndian(sbgp.AsSpan(8)), BinaryPrimitives.ReadUInt32BigEndian(sbgp.AsSpan(12)), BinaryPrimitives.ReadUInt32BigEndian(sbgp.AsSpan(16)))).IsEqualTo(("roll", 1u, 136u, 1u));
			await Assert.That(Mp4Inspector.Find(tracks[0], "mdia", "minf", "stbl", "sbgp")).IsNull();
		}
	}

	[Test]
	public async Task ChaptersAreWrittenBothAsATextTrackAndNeroChaptersAsync()
	{
		using TestDirectory directory = new();
		string longTitle = "高能：" + new string('哈', 100);
		MediaChapter[] chapters = [.. Chapters, new(TimeSpan.FromSeconds(200), TimeSpan.FromHours(2), longTitle)];
		string path = directory.WriteAllBytes(HybridSamples.FileName, HybridSamples.Recording());

		HybridMp4Converter.Convert(path, chapters);

		byte[] converted = await File.ReadAllBytesAsync(path);
		TrackTable[] tracks = Mp4Inspector.ReadTracks(converted);
		TrackTable text = tracks.Single(static track => track.Handler is "text");
		byte[] chpl = Mp4Inspector.Get(Mp4Inspector.ReadMovie(converted), "udta", "chpl");
		string[] titles = [.. chapters.Select(static chapter => chapter.Title)];

		using (Assert.Multiple())
		{
			await Assert.That(tracks.Where(static track => track.Handler is not "text").Select(static track => track.ChapterTrackId)).IsEquivalentTo([(uint?)text.TrackId, text.TrackId], CollectionOrdering.Matching);
			await Assert.That((text.Timescale, text.MediaDuration)).IsEqualTo((1000u, 7_200_000L));

			// 只在电影里、不启用，播放器不把它当字幕轨
			await Assert.That(text.Flags).IsEqualTo(0x02u);

			// 每章一个样本，按毫秒，时长到下一章开始，最后一章到它自己的结尾；按电影时间刻度 720000，最后一章的时长放不进 32 位
			await Assert.That(text.TimeToSample).IsEquivalentTo([(1u, 60_500u), (1u, 89_624u), (1u, 49_876u), (1u, 7_000_000u)], CollectionOrdering.Matching);
			await Assert.That(text.Samples.Select(sample => ReadText(converted, sample))).IsEquivalentTo(titles, CollectionOrdering.Matching);

			// Nero 章节按 100 纳秒计，太长的标题截在 255 字节以内的字符边界上
			await Assert.That(ReadNeroChapters(chpl)).IsEquivalentTo(
			[
				(0L, "测试标题"),
				(605_000_000L, "高能：哈哈哈"),
				(1_501_239_000L, "最后"),
				(2_000_000_000L, "高能：" + new string('哈', 82))
			], CollectionOrdering.Matching);

			// 章节比音视频长，电影时长到最后一章的结尾
			await Assert.That(Mp4Inspector.ReadMovieHeader(converted)).IsEqualTo((720_000u, 7200L * 720_000));
		}
	}

	// 录制中途写失败时最后一个分片的数据不完整，没写完的样本丢掉
	[Test]
	public async Task SamplesCutOffByTheEndOfTheFileAreDroppedAsync()
	{
		using TestDirectory directory = new();
		byte[] original = Truncate(HybridSamples.Recording(), Truncation.InsideLastMediaData);
		string path = directory.WriteAllBytes(HybridSamples.FileName, original);

		HybridConversionResult result = HybridMp4Converter.Convert(path, []);

		byte[] converted = await File.ReadAllBytesAsync(path);
		TrackTable[] tracks = Mp4Inspector.ReadTracks(converted);

		using (Assert.Multiple())
		{
			await Assert.That(result).IsTypeOf<HybridConversionResult.Converted>().And.Member(static converted => converted.DroppedSamples, static dropped => dropped.IsGreaterThan(0));
			await Assert.That(tracks.SelectMany(static track => track.Samples).Where(sample => sample.Offset + sample.Size > original.Length)).IsEmpty();
		}

		await AssertSamplesAsync(converted, tracks);
	}

	// 分片挪到 4 GB 之后：块偏移改用 co64，mdat 头本来就是 64 位
	[Test]
	[Timeout(120_000)]
	public async Task OffsetsBeyondFourGigabytesUseLongChunkOffsetsAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string path = directory.Combine("big_fmp4.mp4");
		const long gap = 5L << 30;
		byte[] first = [.. HybridSamples.Init(), .. HybridSamples.Fragment(1, new RunSpec(HybridSamples.VideoTrack, 0, HybridSamples.Video(10)), new RunSpec(HybridSamples.AudioTrack, 0, HybridSamples.Audio(10)))];
		byte[] last = HybridSamples.Fragment(2, new RunSpec(HybridSamples.VideoTrack, 30_000, HybridSamples.Video(10, firstIndex: 10)), new RunSpec(HybridSamples.AudioTrack, 10_240, HybridSamples.Audio(10)));

		await using (FileStream file = new(path, FileMode.CreateNew, FileAccess.Write))
		{
			await file.WriteAsync(first, cancellationToken);
		}

		await MakeSparseAsync(path, cancellationToken);

		await using (FileStream file = new(path, FileMode.Open, FileAccess.Write))
		{
			file.Position = first.Length;
			byte[] hole = [.. Fmp4Samples.Words(1), .. "free"u8, .. HybridSamples.UInt64(gap)];
			await file.WriteAsync(hole, cancellationToken);
			file.Position = first.Length + gap;
			await file.WriteAsync(last, cancellationToken);
		}

		HybridConversionResult result = HybridMp4Converter.Convert(path, []);

		using SafeFileHandle handle = File.OpenHandle(path);
		long length = RandomAccess.GetLength(handle);
		byte[] tail = new byte[length - (first.Length + gap + last.Length)];
		RandomAccess.Read(handle, tail, first.Length + gap + last.Length);
		TrackTable[] tracks = Mp4Inspector.ReadTracksFromMovie(tail.AsSpan(8).ToArray());

		using (Assert.Multiple())
		{
			await Assert.That(result).IsTypeOf<HybridConversionResult.Converted>();
			await Assert.That(tracks.Where(static track => !track.HasLongOffsets).Select(static track => track.TrackId)).IsEmpty();
			await Assert.That(tracks[0].Samples[10].Offset).IsGreaterThan(uint.MaxValue);

			foreach (TrackTable track in tracks)
			{
				await Assert.That(track.Samples.Where(sample => !IsPayload(Read(handle, sample), track.TrackId))).IsEmpty();
			}
		}
	}

	public enum Truncation
	{
		None,
		InsideLastMediaData,
		InsideLastMovieFragment,
		AfterLastMovieFragment
	}

	public enum Interruption
	{
		TornAppend,
		AfterAppend,
		AfterFlip
	}

	public enum Corruption
	{
		ChunkOffsetOnTheFileType,
		MediaSampleDuration,
		ChapterSampleDuration
	}

	private static byte[] Truncate(byte[] recording, Truncation truncation)
	{
		// 样本内容里不会出现 moof 这几个字节
		int lastFragment = recording.AsSpan().LastIndexOf("moof"u8) - 4;
		int length = truncation switch
		{
			Truncation.None => recording.Length,
			Truncation.InsideLastMediaData => recording.Length - 1500,
			Truncation.InsideLastMovieFragment => lastFragment + 100,
			Truncation.AfterLastMovieFragment => lastFragment + (int)BinaryPrimitives.ReadUInt32BigEndian(recording.AsSpan(lastFragment)),
			_ => throw new UnreachableException()
		};

		return recording.AsSpan(0, length).ToArray();
	}

	private static byte[] RecordingAfterFree(int freePayload)
	{
		return [.. HybridSamples.FileType(), .. Fmp4Samples.Box("free", new byte[freePayload]), .. HybridSamples.Recording().AsSpan(HybridSamples.FileType().Length)];
	}

	private static HybridMp4Converter Prepare(SafeFileHandle handle, IReadOnlyList<MediaChapter> chapters)
	{
		return FragmentedFileReader.Read(handle) is FragmentedFile.Fragmented { Layout: var layout } ? new HybridMp4Converter(handle, layout, chapters) : throw new InvalidOperationException("the sample is not fragmented");
	}

	private static FragmentedFile ReadLayout(string path)
	{
		using SafeFileHandle handle = File.OpenHandle(path);
		return FragmentedFileReader.Read(handle);
	}

	// 样本开头写着轨道号、所在 trun 的 tfdt 和样本在 trun 里的序号，其余字节由它们决定
	// 按样本表读回来，每个样本的内容都对得上，(tfdt, 序号) 严格递增，样本没有错位也没有调换顺序
	private static async Task AssertSamplesAsync(byte[] file, TrackTable[] tracks)
	{
		foreach (TrackTable track in tracks.Where(static track => track.Handler is not "text"))
		{
			using (Assert.Multiple())
			{
				await Assert.That(track.Samples.Where(sample => !IsPayload(file.AsSpan((int)sample.Offset, (int)sample.Size), track.TrackId))).IsEmpty();
				await Assert.That(track.Samples.Select(sample => ReadSampleKey(file.AsSpan((int)sample.Offset)))).IsInOrder().And.HasDistinctItems();
				await Assert.That(track.TimeToSample.Sum(static entry => entry.Count)).IsEqualTo(track.Samples.Length);
			}
		}
	}

	private static bool IsPayload(ReadOnlySpan<byte> data, uint trackId)
	{
		return data.Length >= 12 && data.SequenceEqual(HybridSamples.Payload(trackId, BinaryPrimitives.ReadUInt32BigEndian(data.Slice(4)), (int)BinaryPrimitives.ReadUInt32BigEndian(data.Slice(8)), data.Length));
	}

	private static (uint DecodeTime, uint Index) ReadSampleKey(ReadOnlySpan<byte> data)
	{
		return (BinaryPrimitives.ReadUInt32BigEndian(data.Slice(4)), BinaryPrimitives.ReadUInt32BigEndian(data.Slice(8)));
	}

	private static byte[] Read(SafeFileHandle handle, (long Offset, uint Size) sample)
	{
		byte[] data = new byte[sample.Size];
		RandomAccess.Read(handle, data, sample.Offset);
		return data;
	}

	// 两字节长度、UTF-8 标题，后面跟一个 12 字节的 encd 盒子
	private static string ReadText(byte[] file, (long Offset, uint Size) sample)
	{
		int length = BinaryPrimitives.ReadUInt16BigEndian(file.AsSpan((int)sample.Offset));
		return sample.Size == 2 + length + 12 && file.AsSpan((int)sample.Offset + 2 + length + 4, 4).SequenceEqual("encd"u8) ? Encoding.UTF8.GetString(file, (int)sample.Offset + 2, length) : "<malformed>";
	}

	private static (long Start, string Title)[] ReadNeroChapters(byte[] chpl)
	{
		List<(long, string)> chapters = [];
		int offset = 9;

		for (int index = 0; index < chpl[8]; ++index)
		{
			long start = (long)BinaryPrimitives.ReadUInt64BigEndian(chpl.AsSpan(offset));
			int length = chpl[offset + 8];
			chapters.Add((start, Encoding.UTF8.GetString(chpl, offset + 9, length)));
			offset += 9 + length;
		}

		return [.. chapters];
	}

	// Windows 上要先标成稀疏文件，中间几个 GB 的空洞才不占磁盘也不用填零；其他系统的文件系统默认就是稀疏的
	private static async Task MakeSparseAsync(string path, CancellationToken cancellationToken)
	{
		if (!OperatingSystem.IsWindows())
		{
			return;
		}

		using Process process = Process.Start(new ProcessStartInfo("fsutil", ["sparse", "setflag", path]) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true }) ?? throw new InvalidOperationException("fsutil did not start");
		await process.WaitForExitAsync(cancellationToken);
		Skip.When(process.ExitCode is not 0, "fsutil cannot mark the file sparse");
	}
}
