using System.Buffers.Binary;
using System.Text;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Fakes;

// 带填充数据 NAL 的 FLV 视频 tag 和 fMP4 分片。同一组样本去掉填充之后再拼一遍，就是去填充应得的结果，长度字段都由这里重新计算
internal static class FillerSamples
{
	public static byte[] Picture(FillerCodec codec, bool isKey, string marker)
	{
		byte[] header = codec is FillerCodec.Avc ? [(byte)(isKey ? 0x65 : 0x41)] : [(byte)((isKey ? 19 : 1) << 1), 1];
		return [.. header, .. Encoding.ASCII.GetBytes(marker)];
	}

	// 0xFF 串加上 rbsp 结尾
	public static byte[] Filler(FillerCodec codec, int length)
	{
		byte[] header = codec is FillerCodec.Avc ? [12] : [38 << 1, 1];
		byte[] filler = new byte[header.Length + length + 1];
		header.CopyTo(filler, 0);
		filler.AsSpan(header.Length, length).Fill(0xFF);
		filler[^1] = 0x80;
		return filler;
	}

	// 按另一种编码的规则会被当成填充的画面 NAL：AVC 的类型 13 头字节 0x4D 按 HEVC 算是 38，HEVC 的 RADL_N 头字节 0x0C 按 AVC 算是 12
	public static byte[] Lookalike(FillerCodec codec, string marker)
	{
		byte[] header = codec is FillerCodec.Avc ? [0x4D] : [6 << 1, 1];
		return [.. header, .. Encoding.ASCII.GetBytes(marker)];
	}

	public static bool IsFiller(FillerCodec codec, byte[] nal)
	{
		return codec is FillerCodec.Avc ? (nal[0] & 0x1F) is 12 : (nal[0] >> 1 & 0x3F) is 38;
	}

	// 长度前缀加上 NAL
	public static byte[] Sample(int lengthSize, IEnumerable<byte[]> nals)
	{
		List<byte> sample = [];

		foreach (byte[] nal in nals)
		{
			for (int shift = (lengthSize - 1) * 8; shift >= 0; shift -= 8)
			{
				sample.Add((byte)(nal.Length >> shift));
			}

			sample.AddRange(nal);
		}

		return [.. sample];
	}

	// 去掉填充之后还剩下 NAL 才去
	public static byte[][] WithoutFiller(FillerCodec codec, byte[][] nals)
	{
		byte[][] kept = [.. nals.Where(nal => !IsFiller(codec, nal))];
		return kept.Length is 0 ? nals : kept;
	}

	public static FillerCodec GetCodec(FlvFillerStyle style)
	{
		return style is FlvFillerStyle.LegacyHevc or FlvFillerStyle.EnhancedHevc ? FillerCodec.Hevc : FillerCodec.Avc;
	}

	public static byte[] DecoderConfiguration(FillerCodec codec, int lengthSize)
	{
		byte[] record = new byte[codec is FillerCodec.Avc ? 7 : 23];
		record[0] = 1;
		record[codec is FillerCodec.Avc ? 4 : 21] = (byte)(0xFC | lengthSize - 1);
		return record;
	}

	public static byte[] FlvSequence(FlvFillerStyle style, int lengthSize)
	{
		return FlvSamples.Tag(FlvSamples.VideoTag, 0, [.. FlvVideoPrefix(style, true, true), .. DecoderConfiguration(GetCodec(style), lengthSize)]);
	}

	public static byte[] FlvFrame(FlvFillerStyle style, bool isKey, uint timestamp, int lengthSize, byte[][] nals)
	{
		return FlvSamples.Tag(FlvSamples.VideoTag, timestamp, [.. FlvVideoPrefix(style, isKey, false), .. Sample(lengthSize, nals)]);
	}

	// 传统格式和国内扩展的 HEVC 带包类型和合成时间；Enhanced FLV 的 CodedFrames 带合成时间，CodedFramesX 不带
	public static byte[] FlvVideoPrefix(FlvFillerStyle style, bool isKey, bool isSequence)
	{
		byte frameType = (byte)(isKey ? 1 : 2);
		byte packetType = isSequence ? (byte)0 : (byte)1;
		byte[] compositionTime = isSequence ? [0, 0, 0] : [0, 0, 0x21];

		return style switch
		{
			FlvFillerStyle.Avc => [(byte)(frameType << 4 | 7), packetType, .. compositionTime],
			FlvFillerStyle.LegacyHevc => [(byte)(frameType << 4 | 12), packetType, .. compositionTime],
			FlvFillerStyle.EnhancedHevc => [(byte)(0x80 | frameType << 4 | packetType), .. "hvc1"u8, .. isSequence ? [] : compositionTime],
			_ => [(byte)(0x80 | frameType << 4 | (isSequence ? 0 : 3)), .. "avc1"u8]
		};
	}

	/// <param name="isAudioFirst">音频轨排在视频轨之前，并带有 mp4a 样本项。</param>
	public static byte[] Init(FillerCodec codec, int lengthSize, string? sampleEntry = null, bool isAudioFirst = false)
	{
		byte[] entry = new byte[78];
		byte[] config = Fmp4Samples.Box(codec is FillerCodec.Avc ? "avcC" : "hvcC", DecoderConfiguration(codec, lengthSize));
		byte[] stsd = [0, 0, 0, 0, 0, 0, 0, 1, .. Fmp4Samples.Box(sampleEntry ?? (codec is FillerCodec.Avc ? "avc1" : "hvc1"), [.. entry, .. config])];
		byte[] videoMedia = [.. Handler("vide"), .. Fmp4Samples.Box("minf", Fmp4Samples.Box("stbl", Fmp4Samples.Box("stsd", stsd)))];
		byte[] video = Fmp4Samples.Box("trak", [.. TrackHeader(Fmp4Samples.VideoTrack), .. Fmp4Samples.Box("mdia", videoMedia)]);
		byte[] audioStsd = [0, 0, 0, 0, 0, 0, 0, 1, .. Fmp4Samples.Box("mp4a", new byte[28])];
		byte[] audioMedia = isAudioFirst ? [.. Handler("soun"), .. Fmp4Samples.Box("minf", Fmp4Samples.Box("stbl", Fmp4Samples.Box("stsd", audioStsd)))] : Handler("soun");
		byte[] audio = Fmp4Samples.Box("trak", [.. TrackHeader(Fmp4Samples.AudioTrack), .. Fmp4Samples.Box("mdia", audioMedia)]);
		byte[][] tracks = isAudioFirst ? [audio, video] : [video, audio];

		return
		[
			.. Fmp4Samples.Box("ftyp", "isom"u8.ToArray()),
			.. Fmp4Samples.Box("moov",
			[
				.. tracks.SelectMany(static track => track),
				.. Fmp4Samples.Box("mvex", [.. Trex(Fmp4Samples.VideoTrack), .. Trex(Fmp4Samples.AudioTrack)])
			])
		];
	}

	// 和 B 站的分片一样：mfhd 加两个 traf，视频 tfhd 0x020022、trun 0xb05，音频 tfhd 0x02002a、trun 0x205；mdat 紧跟 moof，视频数据在前
	public static byte[] Fragment(byte[][] video, byte[][] audio, Fmp4Layout layout = Fmp4Layout.Bilibili)
	{
		bool isLarge = layout is Fmp4Layout.LargeMdat;
		bool isAudioFirst = layout is Fmp4Layout.AudioFirst;
		int mdatHeaderSize = isLarge ? 16 : 8;
		int moofLength = Moof(video, audio, layout, 0, 0).Length;
		int audioLength = audio.Sum(static sample => sample.Length);
		int videoOffset = moofLength + mdatHeaderSize + (isAudioFirst ? audioLength : 0);
		int videoLength = video.Sum(static sample => sample.Length);
		int audioOffset = layout switch
		{
			Fmp4Layout.AudioFirst => moofLength + mdatHeaderSize,
			Fmp4Layout.OverlappingRuns => videoOffset,
			Fmp4Layout.DataOutsideMdat => videoOffset + videoLength + 1,
			_ => videoOffset + videoLength
		};

		byte[] videoData = [.. video.SelectMany(static sample => sample)];
		byte[] audioData = [.. audio.SelectMany(static sample => sample)];
		byte[] payload = isAudioFirst ? [.. audioData, .. videoData] : [.. videoData, .. audioData];
		byte[] mdat = new byte[mdatHeaderSize + payload.Length];
		"mdat"u8.CopyTo(mdat.AsSpan(4));

		if (isLarge)
		{
			BinaryPrimitives.WriteUInt32BigEndian(mdat, 1);
			BinaryPrimitives.WriteUInt64BigEndian(mdat.AsSpan(8), (ulong)mdat.Length);
		}
		else if (layout is not Fmp4Layout.MdatSizeZero)
		{
			BinaryPrimitives.WriteUInt32BigEndian(mdat, (uint)mdat.Length);
		}

		payload.CopyTo(mdat.AsSpan(mdatHeaderSize));
		return [.. Moof(video, audio, layout, videoOffset, audioOffset), .. mdat];
	}

	private static byte[] Moof(byte[][] video, byte[][] audio, Fmp4Layout layout, int videoOffset, int audioOffset)
	{
		uint videoTfhdFlags = layout switch
		{
			Fmp4Layout.NotBasedOnMoof => 0x00_0022,
			Fmp4Layout.BaseDataOffset => 0x02_0023,
			Fmp4Layout.VideoDefaultSampleSize => 0x02_0032,
			_ => 0x02_0022
		};
		uint videoTrunFlags = layout is Fmp4Layout.VideoDefaultSampleSize ? 0x905u : 0xb05u;

		List<byte> videoTfhd = [.. Word(videoTfhdFlags), .. Word(Fmp4Samples.VideoTrack)];

		if (layout is Fmp4Layout.BaseDataOffset)
		{
			videoTfhd.AddRange([0, 0, 0, 0, .. Word(0)]);
		}

		videoTfhd.AddRange(Word(1));

		if (layout is Fmp4Layout.VideoDefaultSampleSize)
		{
			videoTfhd.AddRange(Word((uint)video[0].Length));
		}

		videoTfhd.AddRange(Word(Fmp4Samples.SyncFlags));

		// 两个视频 trun 时第二个的数据接在第一个后面
		int half = layout is Fmp4Layout.TwoVideoRuns ? (video.Length + 1) / 2 : video.Length;
		byte[] videoTruns = [.. VideoTrun(video[..half], videoOffset), .. half < video.Length ? VideoTrun(video[half..], videoOffset + video[..half].Sum(static sample => sample.Length)) : []];

		byte[] VideoTrun(byte[][] samples, int dataOffset)
		{
			List<byte> trun = [.. Word(videoTrunFlags), .. Word((uint)samples.Length), .. Word((uint)dataOffset), .. Word(Fmp4Samples.SyncFlags)];

			foreach (byte[] sample in samples)
			{
				trun.AddRange(Word(1500));

				if ((videoTrunFlags & 0x200) is not 0)
				{
					trun.AddRange(Word((uint)sample.Length));
				}

				trun.AddRange(Word(0));
			}

			return Fmp4Samples.Box("trun", [.. trun]);
		}

		// 样本数大得离谱的音频 trun 只带 data_offset，样本大小取 tfhd 里的默认值
		bool isHugeAudioRun = layout is Fmp4Layout.HugeAudioSampleCount;
		List<byte> audioTrun = isHugeAudioRun ? [.. Word(0x001), .. Word(0x8000_0000), .. Word((uint)audioOffset)] : [.. Word(0x205), .. Word((uint)audio.Length), .. Word((uint)audioOffset), .. Word(Fmp4Samples.SyncFlags)];

		if (!isHugeAudioRun)
		{
			foreach (byte[] sample in audio)
			{
				audioTrun.AddRange(Word((uint)sample.Length));
			}
		}

		byte[] audioTfhd = isHugeAudioRun ? [.. Word(0x02_003a), .. Word(Fmp4Samples.AudioTrack), .. Word(1), .. Word(1024), .. Word(1), .. Word(Fmp4Samples.SyncFlags)] : [.. Word(0x02_002a), .. Word(Fmp4Samples.AudioTrack), .. Word(1), .. Word(1024), .. Word(Fmp4Samples.SyncFlags)];

		byte[] videoTraf = Fmp4Samples.Box("traf",
		[
			.. Fmp4Samples.Box("tfhd", [.. videoTfhd]),
			.. Fmp4Samples.Box("tfdt", new byte[8]),
			.. videoTruns,
			.. layout is Fmp4Layout.Encrypted ? Fmp4Samples.Box("senc", new byte[8]) : []
		]);
		byte[] audioTraf = Fmp4Samples.Box("traf",
		[
			.. Fmp4Samples.Box("tfhd", audioTfhd),
			.. Fmp4Samples.Box("tfdt", new byte[8]),
			.. Fmp4Samples.Box("trun", [.. audioTrun])
		]);

		return Fmp4Samples.Box("moof", [.. Fmp4Samples.Box("mfhd", [.. Word(0), .. Word(1)]), .. videoTraf, .. audioTraf]);
	}

	private static byte[] TrackHeader(uint trackId)
	{
		byte[] tkhd = new byte[24];
		BinaryPrimitives.WriteUInt32BigEndian(tkhd.AsSpan(12), trackId);
		return Fmp4Samples.Box("tkhd", tkhd);
	}

	private static byte[] Handler(string handler)
	{
		byte[] hdlr = new byte[24];
		Encoding.ASCII.GetBytes(handler, hdlr.AsSpan(8));
		return Fmp4Samples.Box("hdlr", hdlr);
	}

	// default_sample_size 是 0，音视频 trun 里都要有样本大小
	private static byte[] Trex(uint trackId)
	{
		byte[] trex = new byte[24];
		BinaryPrimitives.WriteUInt32BigEndian(trex.AsSpan(4), trackId);
		BinaryPrimitives.WriteUInt32BigEndian(trex.AsSpan(8), 1);
		return Fmp4Samples.Box("trex", trex);
	}

	private static byte[] Word(uint value)
	{
		byte[] bytes = new byte[4];
		BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
		return bytes;
	}
}

public enum FillerCodec
{
	Avc,
	Hevc
}

public enum FlvFillerStyle
{
	Avc,
	LegacyHevc,
	EnhancedHevc,
	EnhancedAvcWithoutCompositionTime
}

public enum Fmp4Layout
{
	Bilibili,
	LargeMdat,
	AudioFirst,
	TwoVideoRuns,
	HugeAudioSampleCount,
	NotBasedOnMoof,
	BaseDataOffset,
	VideoDefaultSampleSize,
	Encrypted,
	MdatSizeZero,
	DataOutsideMdat,
	OverlappingRuns
}
