using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using static BilibiliLiveRecordDownLoader.Application.Tests.Fakes.Fmp4Samples;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Fakes;

internal sealed record SampleSpec(uint Duration, int Size, bool IsSync = true, int CompositionOffset = 0);

// DataOffset 为 null 时数据紧跟在 moof 后面的 mdat 里
internal sealed record RunSpec(uint TrackId, ulong DecodeTime, SampleSpec[] Samples, int? DataOffset = null);

// 按 B 站 HLS 分片的结构造 fMP4：tfhd 以 moof 为基准，trun 版本 1 带 data_offset、first_sample_flags 和逐个样本的时长、大小、合成偏移
// 样本内容按轨道和样本序号填充，转换后能按样本表把每个样本读回来核对
internal static class HybridSamples
{
	public const uint VideoTrack = 1;
	public const uint AudioTrack = 2;
	public const uint VideoTimescale = 90000;
	public const uint AudioTimescale = 48000;

	// 和录下的 fMP4 一样带开始时间和 _fmp4 后缀
	public const string FileName = "20260102_030405_fmp4.mp4";

	public static byte[] Init(string videoCodec = "avc1", bool hasAudio = true, byte[]? audioEditList = null)
	{
		List<byte[]> movie = [MovieHeader(), Track(VideoTrack, "vide", VideoTimescale, SampleEntry(videoCodec), null)];

		if (hasAudio)
		{
			movie.Add(Track(AudioTrack, "soun", AudioTimescale, AudioSampleEntry(), audioEditList));
		}

		movie.Add(Box("mvex", [.. Trex(VideoTrack), .. hasAudio ? Trex(AudioTrack) : []]));
		return [.. FileType(), .. Box("moov", [.. movie.SelectMany(static box => box)])];
	}

	// 三个分片，B 站的结构：音频轨在每个分片里第一个样本标成非同步；总长超过后处理的最小长度
	public static byte[] Recording()
	{
		return
		[
			.. Init(),
			.. Fragment(1, new RunSpec(VideoTrack, 0, Video(30)), new RunSpec(AudioTrack, 0, Audio(42))),
			.. Fragment(2, new RunSpec(VideoTrack, 90_000, Video(30, firstIndex: 30)), new RunSpec(AudioTrack, 42 * 1024, Audio(47))),
			.. Fragment(3, new RunSpec(VideoTrack, 180_000, Video(30, firstIndex: 60)), new RunSpec(AudioTrack, 89 * 1024, Audio(47)))
		];
	}

	// 和 B 站一样 32 字节：isom 主品牌加四个兼容品牌
	public static byte[] FileType()
	{
		return Box("ftyp", [.. "isom"u8, 0, 0, 0, 1, .. "isommp42avc1dash"u8]);
	}

	public static byte[] Fragment(uint sequence, params RunSpec[] runs)
	{
		List<byte> data = [];
		List<int> dataStarts = [];

		foreach (RunSpec run in runs)
		{
			dataStarts.Add(data.Count);

			for (int index = 0; index < run.Samples.Length; ++index)
			{
				data.AddRange(Payload(run.TrackId, run.DecodeTime, index, run.Samples[index].Size));
			}
		}

		// 先按 data_offset 为 0 算出 moof 的大小，再回填
		int moofSize = BuildMovieFragment(sequence, runs, new int[runs.Length]).Length;
		int[] offsets = [.. runs.Select((run, index) => run.DataOffset ?? moofSize + 8 + dataStarts[index])];
		return [.. BuildMovieFragment(sequence, runs, offsets), .. Box("mdat", [.. data])];
	}

	// 样本前 12 字节写轨道、解码时间和序号，其余按序号填充
	public static byte[] Payload(uint trackId, ulong decodeTime, int index, int size)
	{
		byte[] payload = new byte[size];

		for (int i = 0; i < size; ++i)
		{
			payload[i] = (byte)(trackId * 31 + (ulong)index * 7 + decodeTime + (ulong)i);
		}

		if (size >= 12)
		{
			BinaryPrimitives.WriteUInt32BigEndian(payload, trackId);
			BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(4), (uint)decodeTime);
			BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(8), (uint)index);
		}

		return payload;
	}

	public static SampleSpec[] Video(int count, int size = 1000, int keyFrameInterval = 30, int firstIndex = 0, uint duration = 3000)
	{
		return [.. Enumerable.Range(firstIndex, count).Select(index => new SampleSpec(duration, size + index % 5, index % keyFrameInterval is 0, index % 3 is 0 ? 6000 : 3000))];
	}

	public static SampleSpec[] Audio(int count, int size = 300)
	{
		return [.. Enumerable.Range(0, count).Select(index => new SampleSpec(1024, size + index % 3, index is not 0))];
	}

	public static byte[] FullBox(string type, byte version, uint flags, byte[] payload)
	{
		return Box(type, [.. Words((uint)version << 24 | flags), .. payload]);
	}

	public static byte[] UInt64(ulong value)
	{
		byte[] bytes = new byte[8];
		BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
		return bytes;
	}

	// 一段从 mediaTime 开始的媒体编辑，时长 0：init 段写出时分片还没有
	public static byte[] EditList(int mediaTime, uint rate = 0x0001_0000)
	{
		return Box("edts", FullBox("elst", 0, 0, Words(1, 0, (uint)mediaTime, rate)));
	}

	private static byte[] BuildMovieFragment(uint sequence, RunSpec[] runs, int[] offsets)
	{
		List<byte> moof = [.. FullBox("mfhd", 0, 0, Words(sequence))];

		for (int index = 0; index < runs.Length; ++index)
		{
			RunSpec run = runs[index];

			// 第一个样本之后的同步属性都相同时和 B 站一样用 first_sample_flags 加 tfhd 的默认标志，否则逐个样本写标志
			bool isUniform = run.Samples.Skip(1).Select(static sample => sample.IsSync).Distinct().Count() <= 1;
			uint defaultFlags = run.Samples.Skip(1).All(static sample => sample.IsSync) ? SyncFlags : NonSyncFlags;
			List<uint> trun = [(uint)run.Samples.Length, (uint)offsets[index]];

			if (isUniform)
			{
				trun.Add(run.Samples is [{ IsSync: true }, ..] ? SyncFlags : NonSyncFlags);
			}

			foreach (SampleSpec sample in run.Samples)
			{
				trun.Add(sample.Duration);
				trun.Add((uint)sample.Size);

				if (!isUniform)
				{
					trun.Add(sample.IsSync ? SyncFlags : NonSyncFlags);
				}

				trun.Add((uint)sample.CompositionOffset);
			}

			moof.AddRange(Box("traf",
			[
				.. FullBox("tfhd", 0, 0x02_0020, Words(run.TrackId, defaultFlags)),
				.. FullBox("tfdt", 1, 0, UInt64(run.DecodeTime)),
				.. FullBox("trun", 1, isUniform ? 0x0B05u : 0x0F01u, Words(CollectionsMarshal.AsSpan(trun)))
			]));
		}

		return Box("moof", [.. moof]);
	}

	private static byte[] MovieHeader()
	{
		byte[] content = new byte[96];
		BinaryPrimitives.WriteUInt32BigEndian(content.AsSpan(8), 1000);
		BinaryPrimitives.WriteUInt32BigEndian(content.AsSpan(16), 0x0001_0000);
		BinaryPrimitives.WriteUInt16BigEndian(content.AsSpan(20), 0x0100);
		BinaryPrimitives.WriteUInt32BigEndian(content.AsSpan(32), 0x0001_0000);
		BinaryPrimitives.WriteUInt32BigEndian(content.AsSpan(48), 0x0001_0000);
		BinaryPrimitives.WriteUInt32BigEndian(content.AsSpan(64), 0x4000_0000);
		BinaryPrimitives.WriteUInt32BigEndian(content.AsSpan(92), 3);
		return FullBox("mvhd", 0, 0, content);
	}

	private static byte[] Track(uint trackId, string handler, uint timescale, byte[] sampleEntry, byte[]? editList)
	{
		byte[] tkhd = new byte[80];
		BinaryPrimitives.WriteUInt32BigEndian(tkhd.AsSpan(8), trackId);
		BinaryPrimitives.WriteUInt32BigEndian(tkhd.AsSpan(36), 0x0001_0000);
		BinaryPrimitives.WriteUInt32BigEndian(tkhd.AsSpan(52), 0x0001_0000);
		BinaryPrimitives.WriteUInt32BigEndian(tkhd.AsSpan(68), 0x4000_0000);
		byte[] mdhd = new byte[20];
		BinaryPrimitives.WriteUInt32BigEndian(mdhd.AsSpan(8), timescale);
		BinaryPrimitives.WriteUInt16BigEndian(mdhd.AsSpan(16), 0x55C4);
		byte[] hdlr = [0, 0, 0, 0, .. Encoding.ASCII.GetBytes(handler), 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, .. "Handler\0"u8];
		byte[] mediaHeader = handler is "vide" ? FullBox("vmhd", 0, 1, new byte[8]) : FullBox("smhd", 0, 0, new byte[4]);
		byte[] stbl = Box("stbl",
		[
			.. FullBox("stsd", 0, 0, [.. Words(1), .. sampleEntry]),
			.. FullBox("stts", 0, 0, Words(0)),
			.. FullBox("stsc", 0, 0, Words(0)),
			.. FullBox("stsz", 0, 0, Words(0, 0)),
			.. FullBox("stco", 0, 0, Words(0))
		]);
		byte[] minf = Box("minf", [.. mediaHeader, .. Box("dinf", FullBox("dref", 0, 0, [.. Words(1), .. FullBox("url ", 0, 1, [])])), .. stbl]);
		byte[] mdia = Box("mdia", [.. FullBox("mdhd", 0, 0, mdhd), .. FullBox("hdlr", 0, 0, hdlr), .. minf]);
		return Box("trak", [.. FullBox("tkhd", 0, 3, tkhd), .. editList ?? [], .. mdia]);
	}

	private static byte[] SampleEntry(string codec)
	{
		byte[] fields = new byte[78];
		BinaryPrimitives.WriteUInt16BigEndian(fields.AsSpan(6), 1);
		BinaryPrimitives.WriteUInt16BigEndian(fields.AsSpan(24), 1280);
		BinaryPrimitives.WriteUInt16BigEndian(fields.AsSpan(26), 720);
		return Box(codec, fields);
	}

	// AudioSampleEntry 加上 esds，DecoderConfigDescriptor 的 objectTypeIndication 0x40 是 AAC
	private static byte[] AudioSampleEntry()
	{
		byte[] fields = new byte[28];
		BinaryPrimitives.WriteUInt16BigEndian(fields.AsSpan(6), 1);
		BinaryPrimitives.WriteUInt16BigEndian(fields.AsSpan(16), 2);
		BinaryPrimitives.WriteUInt16BigEndian(fields.AsSpan(18), 16);
		BinaryPrimitives.WriteUInt32BigEndian(fields.AsSpan(24), AudioTimescale << 16);
		byte[] decoderSpecific = [0x05, 0x02, 0x11, 0x90];
		byte[] decoderConfig = [0x04, (byte)(13 + decoderSpecific.Length), 0x40, 0x15, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, .. decoderSpecific];
		byte[] stream = [0x03, (byte)(3 + decoderConfig.Length + 3), 0, 1, 0, .. decoderConfig, 0x06, 0x01, 0x02];
		return Box("mp4a", [.. fields, .. FullBox("esds", 0, 0, stream)]);
	}

	private static byte[] Trex(uint trackId)
	{
		return FullBox("trex", 0, 0, Words(trackId, 1, 0, 0, 0));
	}
}
