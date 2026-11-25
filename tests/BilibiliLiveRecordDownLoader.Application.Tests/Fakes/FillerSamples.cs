using System.Buffers.Binary;
using System.Text;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Fakes;

internal static class FillerSamples
{
	public const int AvFrameObu = 6;
	public const int AvPaddingObu = 15;

	public static byte[] Picture(FillerCodec codec, bool isKey, string marker)
	{
		return codec switch
		{
			FillerCodec.Avc => [(byte)(isKey ? 0x65 : 0x41), .. Encoding.ASCII.GetBytes(marker)],
			FillerCodec.Hevc => [(byte)((isKey ? 19 : 1) << 1), 1, .. Encoding.ASCII.GetBytes(marker)],
			FillerCodec.Av1 => Obu(AvFrameObu, [(byte)(isKey ? 1 : 0), .. Encoding.ASCII.GetBytes(marker)])
		};
	}

	// NAL 是 0xFF 串加上 rbsp 结尾
	public static byte[] Filler(FillerCodec codec, int length)
	{
		if (codec is FillerCodec.Av1)
		{
			return Obu(AvPaddingObu, [.. Enumerable.Repeat((byte)0xFF, length)]);
		}

		byte[] header = codec is FillerCodec.Avc ? [12] : [38 << 1, 1];
		byte[] filler = new byte[header.Length + length + 1];
		header.CopyTo(filler, 0);
		filler.AsSpan(header.Length, length).Fill(0xFF);
		filler[^1] = 0x80;
		return filler;
	}

	// 按另一种编码的规则会被当成填充的画面 NAL：AVC 的类型 13 头字节 0x4D 按 HEVC 算是 38，HEVC 的 RADL_N 头字节 0x0C 按 AVC 算是 12
	// AV1 没有这种重叠，换成带扩展头的 OBU：漏读扩展字节就会把它当成大小
	public static byte[] Lookalike(FillerCodec codec, string marker)
	{
		return codec switch
		{
			FillerCodec.Avc => [0x4D, .. Encoding.ASCII.GetBytes(marker)],
			FillerCodec.Hevc => [6 << 1, 1, .. Encoding.ASCII.GetBytes(marker)],
			FillerCodec.Av1 => Obu(AvFrameObu, Encoding.ASCII.GetBytes(marker), extension: 0x10)
		};
	}

	// sizeLength 为 0 时不带大小字段；比实际需要长时按 leb128 允许的写法补 0x80
	public static byte[] Obu(int type, byte[] payload, byte? extension = null, int? sizeLength = null)
	{
		List<byte> obu = [(byte)(type << 3 | (extension is null ? 0 : 0x04) | (sizeLength is 0 ? 0 : 0x02))];

		if (extension is { } value)
		{
			obu.Add(value);
		}

		if (sizeLength is not 0)
		{
			int length = sizeLength ?? Math.Max(1, (32 - int.LeadingZeroCount(payload.Length) + 6) / 7);

			for (int index = 0; index < length; ++index)
			{
				obu.Add((byte)(payload.Length >> index * 7 & 0x7F | (index < length - 1 ? 0x80 : 0)));
			}
		}

		obu.AddRange(payload);
		return [.. obu];
	}

	private static bool IsFiller(FillerCodec codec, byte[] unit)
	{
		return codec switch
		{
			FillerCodec.Avc => (unit[0] & 0x1F) is 12,
			FillerCodec.Hevc => (unit[0] >> 1 & 0x3F) is 38,
			FillerCodec.Av1 => (unit[0] >> 3 & 0x0F) is AvPaddingObu
		};
	}

	// lengthSize 为 0 时直接拼接：AV1 的 OBU 自带大小
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
		// av1C 只有 marker、version 和档次级别这些，没有长度字段宽度
		if (codec is FillerCodec.Av1)
		{
			return [0x81, 0x08, 0x0C, 0x00];
		}

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

	private static byte[] FlvVideoPrefix(FlvFillerStyle style, bool isKey, bool isSequence)
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

	// videoTimescale 为 0 时视频轨不带 mdhd，读不出分片的时间，改写时间戳时原样留下
	public static byte[] Init(FillerCodec codec, int lengthSize, string? sampleEntry = null, uint videoTimescale = 0)
	{
		byte[] entry = new byte[78];
		byte[] mdhd = new byte[24];
		BinaryPrimitives.WriteUInt32BigEndian(mdhd.AsSpan(12), videoTimescale);
		(string defaultEntry, string configType) = codec switch
		{
			FillerCodec.Avc => ("avc1", "avcC"),
			FillerCodec.Hevc => ("hvc1", "hvcC"),
			FillerCodec.Av1 => ("av01", "av1C")
		};
		byte[] config = Fmp4Samples.Box(configType, DecoderConfiguration(codec, lengthSize));
		byte[] stsd = [0, 0, 0, 0, 0, 0, 0, 1, .. Fmp4Samples.Box(sampleEntry ?? defaultEntry, [.. entry, .. config])];
		byte[] videoMedia = [.. videoTimescale is 0 ? [] : Fmp4Samples.Box("mdhd", mdhd), .. Fmp4Samples.Hdlr("vide"), .. Fmp4Samples.Box("minf", Fmp4Samples.Box("stbl", Fmp4Samples.Box("stsd", stsd)))];

		return
		[
			.. Fmp4Samples.Box("ftyp", "isom"u8.ToArray()),
			.. Fmp4Samples.Box
			(
				"moov",
				[
					.. Fmp4Samples.Box("trak", [.. Fmp4Samples.Tkhd(Fmp4Samples.VideoTrack), .. Fmp4Samples.Box("mdia", videoMedia)]),
					.. Fmp4Samples.Track(Fmp4Samples.AudioTrack, "soun"),
					.. Fmp4Samples.Box("mvex", [.. Fmp4Samples.Trex(Fmp4Samples.VideoTrack, defaultDescriptionIndex: 1), .. Fmp4Samples.Trex(Fmp4Samples.AudioTrack, defaultDescriptionIndex: 1)])
				]
			)
		];
	}

	// 和 B 站的分片一样：mfhd 加两个 traf，视频 tfhd 0x020022、trun 0xb05，音频 tfhd 0x02002a、trun 0x205；mdat 紧跟 moof，视频数据在前
	public static byte[] Fragment(byte[][] video, byte[][] audio, Fmp4Layout layout = Fmp4Layout.Bilibili, uint videoDecodeTime = 0)
	{
		bool isLarge = layout is Fmp4Layout.LargeMdat;
		bool isAudioFirst = layout is Fmp4Layout.AudioFirst;
		int mdatHeaderSize = isLarge ? 16 : 8;
		int moofLength = Moof(video, audio, layout, videoDecodeTime, 0, 0).Length;
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
		return [.. Moof(video, audio, layout, videoDecodeTime, videoOffset, audioOffset), .. mdat];
	}

	private static byte[] Moof(byte[][] video, byte[][] audio, Fmp4Layout layout, uint videoDecodeTime, int videoOffset, int audioOffset)
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

		byte[] videoTraf = Fmp4Samples.Box
		(
			"traf",
			[
				.. Fmp4Samples.Box("tfhd", [.. videoTfhd]),
				.. Fmp4Samples.Box("tfdt", [.. Word(0), .. Word(videoDecodeTime)]),
				.. videoTruns,
				.. layout is Fmp4Layout.Encrypted ? Fmp4Samples.Box("senc", new byte[8]) : []
			]
		);
		byte[] audioTraf = Fmp4Samples.Box
		(
			"traf",
			[
				.. Fmp4Samples.Box("tfhd", audioTfhd),
				.. Fmp4Samples.Box("tfdt", new byte[8]),
				.. Fmp4Samples.Box("trun", [.. audioTrun])
			]
		);

		return Fmp4Samples.Box("moof", [.. Fmp4Samples.Box("mfhd", [.. Word(0), .. Word(1)]), .. videoTraf, .. audioTraf]);
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
	Hevc,
	Av1
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
