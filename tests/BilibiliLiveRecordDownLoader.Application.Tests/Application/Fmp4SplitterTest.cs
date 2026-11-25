using BilibiliLiveRecordDownLoader.Application.Recording;
using BilibiliLiveRecordDownLoader.Application.Recording.Splitting;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using System.Buffers.Binary;
using System.IO.Pipelines;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class Fmp4SplitterTest
{
	private static readonly RecordingSplit OneMinute = new RecordingSplit.ByDuration(TimeSpan.FromMinutes(1));

	private static readonly byte[] Mfra = Fmp4Samples.Box("mfra", Fmp4Samples.Box("mfro", new byte[8]));

	// 盒子类型不是可打印字符，之后再也找不到盒子的边界
	private static readonly byte[] Junk = [0, 0, 0, 12, 0xFF, 0xFF, 0xFF, 0xFF, 1, 2, 3, 4];

	// 一个字节一个字节地写时，盒子头的每一种残缺都会遇到
	[Test]
	[Arguments(1)]
	[Arguments(10)]
	[Arguments(1 << 20)]
	public async Task NewFileStartsWithTheHeaderBeforeAFragmentStartingWithASyncSampleAsync(int chunkSize, CancellationToken cancellationToken)
	{
		byte[] init = Fmp4Samples.Init();
		byte[][] fragments = [Fmp4Samples.Fragment(true, marker: "1"), Fmp4Samples.Fragment(false, marker: "2"), Fmp4Samples.Fragment(true, marker: "3"), Fmp4Samples.Fragment(false, marker: "4"), Fmp4Samples.Fragment(true, marker: "5")];
		SplitRecording recording = new();

		await recording.RunAsync(new Fmp4Splitter(), OneMinute, async session =>
		{
			await session.WriteInChunksAsync([.. init, .. fragments[0]], chunkSize);
			session.Advance(TimeSpan.FromMinutes(1));
			await session.WriteInChunksAsync([.. fragments[1], .. fragments[2], .. fragments[3], .. fragments[4], .. Mfra], chunkSize);
		}, cancellationToken);

		await Assert.That(recording.Files).IsEquivalentTo((byte[][])[[.. init, .. fragments[0], .. fragments[1]], [.. init, .. fragments[2], .. fragments[3], .. fragments[4], .. Mfra]], CollectionOrdering.Matching);
	}

	[Test]
	public async Task Fmp4WithoutVideoSplitsBeforeAnyFragmentAsync(CancellationToken cancellationToken)
	{
		byte[] init = Fmp4Samples.Init(hasVideo: false);
		byte[][] fragments = [Fmp4Samples.Fragment(false, marker: "1"), Fmp4Samples.Fragment(false, marker: "2"), Fmp4Samples.Fragment(false, marker: "3")];
		SplitRecording recording = new();

		await recording.RunAsync(new Fmp4Splitter(), OneMinute, async session =>
		{
			await session.WriteAsync([.. init, .. fragments[0]]);
			session.Advance(TimeSpan.FromMinutes(1));
			await session.WriteAsync([.. fragments[1], .. fragments[2]]);
		}, cancellationToken);

		await Assert.That(recording.Files).IsEquivalentTo((byte[][])[[.. init, .. fragments[0]], [.. init, .. fragments[1], .. fragments[2]]], CollectionOrdering.Matching);
	}

	[Test]
	[MethodDataSource(nameof(UnrecognizedData))]
	public async Task UnrecognizedDataStopsSplittingWithoutLosingDataAsync(byte[] head, byte[] junk, CancellationToken cancellationToken)
	{
		byte[] stream = [.. head, .. junk, .. Fmp4Samples.Fragment(true, marker: "2"), .. Fmp4Samples.Fragment(true, marker: "3"), .. Fmp4Samples.Fragment(true, marker: "4")];
		int split = head.Length + junk.Length;
		SplitRecording recording = new();

		await recording.RunAsync(new Fmp4Splitter(), OneMinute, async session =>
		{
			await session.WriteAsync(stream[..split]);
			session.Advance(TimeSpan.FromMinutes(2));
			await session.WriteAsync(stream[split..]);
		}, cancellationToken);

		byte[] file = await Assert.That(recording.Files).HasSingleItem();
		await Assert.That(file).IsEquivalentTo(stream, CollectionOrdering.Matching);
	}

	public static IEnumerable<TestDataRow<Func<(byte[] Head, byte[] Junk)>>> UnrecognizedData()
	{
		yield return new(static () => ([], [.. Fmp4Samples.Box("styp", "msdh"u8.ToArray()), .. Fmp4Samples.Init()]), DisplayName: "开头不是 ftyp");

		foreach ((string name, byte[] junk) in ((string, byte[])[])[("盒子类型不是可打印字符", Junk), ("大小为 0", [0, 0, 0, 0, .. "free"u8]), ("大小比盒子头小", [0, 0, 0, 4, .. "free"u8]), ("64 位大小比盒子头小", [0, 0, 0, 1, .. "free"u8, 0, 0, 0, 0, 0, 0, 0, 15])])
		{
			yield return new(() => ([.. Fmp4Samples.Init(), .. Fmp4Samples.Fragment(true, marker: "1")], junk), DisplayName: name);
		}
	}

	[Test]
	public async Task FragmentLookalikeAfterGivingUpIsWrittenUnchangedAsync(CancellationToken cancellationToken)
	{
		byte[] init = Fmp4Samples.TimedInit(90_000, 1920, 1080, 3000);
		byte[] lookalike = Fmp4Samples.TimedFragment(990_000, 30, 3000);
		SplitRecording recording = new();

		await recording.RunAsync(new Fmp4Splitter(), OneMinute, async session =>
		{
			await session.WriteAsync([.. init, .. Fmp4Samples.TimedFragment(900_000, 30, 3000)]);
			await session.WriteAsync(Junk);
			await session.WriteAsync(lookalike);
		}, cancellationToken);

		await Assert.That(recording.Files).IsEquivalentTo((byte[][])[[.. init, .. Fmp4Samples.TimedFragment(0, 30, 3000), .. Junk, .. lookalike]], CollectionOrdering.Matching);
	}

	// 放弃解析的位置按收到的字节算，前面去掉的填充数据要从改写时间戳的范围里扣掉，否则会改到后面像分片的数据
	[Test]
	public async Task DataAfterGivingUpInALaterFileIsWrittenUnchangedAsync(CancellationToken cancellationToken)
	{
		byte[] init = FillerSamples.Init(FillerCodec.Avc, 4, videoTimescale: 90_000);
		SplitRecording recording = new();

		await recording.RunAsync(new Fmp4Splitter(), SplitRecording.Fixed(OneMinute, removeFiller: true), async session =>
		{
			await session.WriteAsync([.. init, .. Fragment("k1", 900_000, false)]);
			session.Advance(TimeSpan.FromMinutes(1));
			await session.WriteAsync(Fragment("k2", 990_000, false));
			await session.WriteAsync(Junk);
			await session.WriteAsync(Fragment("k3", 1_080_000, false));
		}, cancellationToken);

		await Assert.That(recording.Files).IsEquivalentTo((byte[][])[[.. init, .. Fragment("k1", 0, true)], [.. init, .. Fragment("k2", 0, true), .. Junk, .. Fragment("k3", 1_080_000, false)]], CollectionOrdering.Matching);

		static byte[] Fragment(string marker, uint decodeTime, bool removeFiller)
		{
			byte[] picture = FillerSamples.Picture(FillerCodec.Avc, true, marker);
			return FillerSamples.Fragment([FillerSamples.Sample(4, removeFiller ? [picture] : [picture, FillerSamples.Filler(FillerCodec.Avc, 1000)])], ["aac"u8.ToArray()], videoDecodeTime: decodeTime);
		}
	}

	[Test]
	[Arguments(IncompleteBox.InTheBoxHeader)]
	[Arguments(IncompleteBox.InTheMdatHeader)]
	[Arguments(IncompleteBox.InTheMdat)]
	public async Task IncompleteFragmentAtTheEndIsNotWrittenAsync(IncompleteBox incomplete, CancellationToken cancellationToken)
	{
		byte[] stream = [.. Fmp4Samples.Init(), .. Fmp4Samples.Fragment(true, marker: "1")];
		byte[] last = Fmp4Samples.Fragment(false, marker: "2");
		int length = incomplete switch
		{
			IncompleteBox.InTheBoxHeader => 6,
			IncompleteBox.InTheMdatHeader => (int)BinaryPrimitives.ReadUInt32BigEndian(last) + 6,
			IncompleteBox.InTheMdat => last.Length - 1
		};
		SplitRecording recording = new();

		await recording.RunAsync(new Fmp4Splitter(), OneMinute, session => session.WriteAsync([.. stream, .. last.AsSpan(0, length)]), cancellationToken);

		byte[] file = await Assert.That(recording.Files).HasSingleItem();
		await Assert.That(file).IsEquivalentTo(stream, CollectionOrdering.Matching);
	}

	// 收到的部分已经认不出就不必等，收流结束时照样写出
	[Test]
	[Arguments((byte[])[0, 0, 0, 0], DisplayName = "大小为 0")]
	[Arguments((byte[])[0, 0, 0, 20, (byte)'m', 0xFF], DisplayName = "类型里有不可打印字符")]
	public async Task ShortDataAlreadyUnrecognizedIsWrittenWhenTheStreamEndsAsync(byte[] tail, CancellationToken cancellationToken)
	{
		byte[] stream = [.. Fmp4Samples.Init(), .. Fmp4Samples.Fragment(true, marker: "1"), .. tail];
		SplitRecording recording = new();

		await recording.RunAsync(new Fmp4Splitter(), OneMinute, session => session.WriteAsync(stream), cancellationToken);

		byte[] file = await Assert.That(recording.Files).HasSingleItem();
		await Assert.That(file).IsEquivalentTo(stream, CollectionOrdering.Matching);
	}

	[Test]
	public async Task DataAfterGivingUpIsNotHeldBackAsync(CancellationToken cancellationToken)
	{
		byte[] stream = [.. Fmp4Samples.Init(), .. Fmp4Samples.Fragment(true, marker: "1"), .. Junk];
		Pipe pipe = new();
		await using RecordingSegmenter segmenter = new(new SegmentStarts().CreateWriter(pipe.Writer), new Fmp4Splitter(), SplitRecording.Fixed(OneMinute), TimeProvider.System, NullLogger.Instance, new RecordingTimeline(string.Empty, TimeProvider.System, RoomDefaults.StreamIdleTimeout));

		await segmenter.WriteAsync(stream, cancellationToken);

		await Assert.That(pipe.Reader.TryRead(out ReadResult result) ? result.Buffer.Length : 0).IsEqualTo(stream.Length);
	}
}

public enum IncompleteBox
{
	InTheBoxHeader,
	InTheMdatHeader,
	InTheMdat
}
