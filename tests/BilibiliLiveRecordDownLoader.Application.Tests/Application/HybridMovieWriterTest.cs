using BilibiliLiveRecordDownLoader.Application.Recording.Hybrid;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using System.Buffers.Binary;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class HybridMovieWriterTest
{
	private const uint MissingTrack = 99;

	private static readonly MediaChapter[] Chapters = [new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "一"), new(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), "二")];

	// stsz 的 sample_size 为 0 表示后面跟着逐个样本的大小表，所以全是 0 字节的样本也要写表
	[Test]
	public async Task SamplesOfZeroBytesAreListedInTheSizeTableAsync()
	{
		using TestDirectory directory = new();
		SampleSpec[] audio = [.. Enumerable.Repeat(new SampleSpec(1024, 0), 10)];
		string path = directory.WriteAllBytes(HybridSamples.FileName, [.. HybridSamples.Init(), .. HybridSamples.Fragment(1, new RunSpec(HybridSamples.AudioTrack, 0, audio), new RunSpec(HybridSamples.VideoTrack, 0, HybridSamples.Video(10)))]);

		HybridConversionResult result = HybridMp4Converter.Convert(path, []);

		using (Assert.Multiple())
		{
			await Assert.That(result).IsTypeOf<HybridConversionResult.Converted>();
			await Assert.That(Mp4Inspector.ReadTracks(await File.ReadAllBytesAsync(path))[1].Samples.Select(static sample => sample.Size)).IsEquivalentTo(Enumerable.Repeat(0u, 10), CollectionOrdering.Matching);
		}
	}

	// 样本表在内存里分块存放；时长交替的样本让 stts、ctts、stsz 都跨过好几块
	[Test]
	public async Task TablesLongerThanOneChunkAreWrittenInOrderAsync()
	{
		using TestDirectory directory = new();
		SampleSpec[] video = [.. HybridSamples.Video(ChunkedList<uint>.ChunkSize * 2 + 1, size: 16).Select(static (sample, index) => sample with { Duration = 1500 + (uint)(index % 2) })];
		string path = directory.WriteAllBytes(HybridSamples.FileName, [.. HybridSamples.Init(hasAudio: false), .. HybridSamples.Fragment(1, new RunSpec(HybridSamples.VideoTrack, 0, video))]);

		HybridConversionResult result = HybridMp4Converter.Convert(path, []);

		TrackTable track = Mp4Inspector.ReadTracks(await File.ReadAllBytesAsync(path)).Single();

		using (Assert.Multiple())
		{
			await Assert.That(result).IsTypeOf<HybridConversionResult.Converted>();
			await Assert.That(Expand(track.TimeToSample)).IsEquivalentTo(video.Select(static sample => sample.Duration), CollectionOrdering.Matching);
			await Assert.That(Expand(track.CompositionOffsets)).IsEquivalentTo(video.Select(static sample => sample.CompositionOffset), CollectionOrdering.Matching);
			await Assert.That(track.Samples.Select(static sample => sample.Size)).IsEquivalentTo(video.Select(static sample => (uint)sample.Size), CollectionOrdering.Matching);
		}
	}

	// 源轨道的引用只留指向写出轨道的 ID，有章节时原来的 chap 由指向新章节轨的 chap 代替
	[Test]
	public async Task TrackReferencesOfTheRecordingAreKeptAsync()
	{
		using TestDirectory directory = new();
		byte[] reference = Fmp4Samples.Box("tref",
		[
			.. Fmp4Samples.Box("cdsc", Fmp4Samples.Words(HybridSamples.AudioTrack, MissingTrack)),
			.. Fmp4Samples.Box("hint", Fmp4Samples.Words(MissingTrack)),
			.. Fmp4Samples.Box("chap", Fmp4Samples.Words(HybridSamples.AudioTrack))
		]);
		string path = directory.WriteAllBytes(HybridSamples.FileName, Recording(reference, []));

		HybridMp4Converter.Convert(path, Chapters);

		byte[] converted = await File.ReadAllBytesAsync(path);
		uint textTrackId = Mp4Inspector.ReadTracks(converted).Single(static track => track.Handler is "text").TrackId;
		byte[] references = Mp4Inspector.Get(Mp4Inspector.Children(Mp4Inspector.ReadMovie(converted), "trak").First(), "tref");

		using (Assert.Multiple())
		{
			await Assert.That(Mp4Inspector.Find(references, "cdsc")).IsEquivalentTo(Fmp4Samples.Words(HybridSamples.AudioTrack), CollectionOrdering.Matching);
			await Assert.That(Mp4Inspector.Find(references, "hint")).IsNull();
			await Assert.That(Mp4Inspector.Children(references, "chap").Select(static chap => BinaryPrimitives.ReadUInt32BigEndian(chap))).IsEquivalentTo([textTrackId], CollectionOrdering.Matching);
		}
	}

	// 源 udta 里的其他盒子原样保留，源里的 chpl 由新的章节代替
	[Test]
	public async Task NeroChaptersOfTheRecordingAreReplacedAsync()
	{
		using TestDirectory directory = new();
		byte[] other = Fmp4Samples.Box("xtra", [1, 2, 3, 4]);
		byte[] userData = Fmp4Samples.Box("udta", [.. HybridSamples.FullBox("chpl", 1, 0, new byte[5]), .. other]);
		string path = directory.WriteAllBytes(HybridSamples.FileName, Recording([], userData));

		HybridMp4Converter.Convert(path, Chapters);

		byte[] udta = Mp4Inspector.Get(Mp4Inspector.ReadMovie(await File.ReadAllBytesAsync(path)), "udta");

		using (Assert.Multiple())
		{
			await Assert.That(Mp4Inspector.Find(udta, "xtra")).IsEquivalentTo(other.AsSpan(8).ToArray(), CollectionOrdering.Matching);
			await Assert.That(Mp4Inspector.Children(udta, "chpl").Select(static chpl => chpl[8])).IsEquivalentTo([(byte)Chapters.Length], CollectionOrdering.Matching);
		}
	}

	// 章节轨的 mdhd 和 stts 按毫秒，tkhd 和编辑列表按电影时间刻度；第一章前用空编辑推迟
	[Test]
	public async Task ChapterTrackIsPlacedOnTheMovieTimelineAsync()
	{
		using TestDirectory directory = new();
		string path = directory.WriteAllBytes(HybridSamples.FileName, Recording([], []));

		HybridMp4Converter.Convert(path, Chapters);

		TrackTable text = Mp4Inspector.ReadTracks(await File.ReadAllBytesAsync(path)).Single(static track => track.Handler is "text");

		using (Assert.Multiple())
		{
			await Assert.That(text.Edits).IsEquivalentTo([new EditListEntry(720_000, -1), new EditListEntry(1_440_000, 0)], CollectionOrdering.Matching);
			await Assert.That((text.Duration, text.Timescale, text.MediaDuration)).IsEqualTo((2_160_000L, 1000u, 2000L));
			await Assert.That(text.TimeToSample).IsEquivalentTo([(1u, 1000u), (1u, 1000u)], CollectionOrdering.Matching);
		}
	}

	// 视频轨末尾加上 trackExtra，moov 末尾加上 movieExtra
	private static byte[] Recording(byte[] trackExtra, byte[] movieExtra)
	{
		byte[] movie = Mp4Inspector.ReadMovie(HybridSamples.Init());
		byte[][] tracks = [.. Mp4Inspector.Children(movie, "trak")];
		byte[] moov = Fmp4Samples.Box("moov",
		[
			.. Fmp4Samples.Box("mvhd", Mp4Inspector.Get(movie, "mvhd")),
			.. Fmp4Samples.Box("trak", [.. tracks[0], .. trackExtra]),
			.. Fmp4Samples.Box("trak", tracks[1]),
			.. Fmp4Samples.Box("mvex", Mp4Inspector.Get(movie, "mvex")),
			.. movieExtra
		]);
		return [.. HybridSamples.FileType(), .. moov, .. HybridSamples.Fragment(1, new RunSpec(HybridSamples.VideoTrack, 0, HybridSamples.Video(10)), new RunSpec(HybridSamples.AudioTrack, 0, HybridSamples.Audio(10)))];
	}

	private static IEnumerable<T> Expand<T>((uint Count, T Value)[] runs)
	{
		return runs.SelectMany(static run => Enumerable.Repeat(run.Value, (int)run.Count));
	}
}
