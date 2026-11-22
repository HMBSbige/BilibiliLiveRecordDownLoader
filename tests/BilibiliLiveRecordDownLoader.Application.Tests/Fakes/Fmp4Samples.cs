using System.Buffers.Binary;
using System.Text;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Fakes;

// 只带切分需要看的盒子：init 段的轨道和 trex，分片的 tfhd、trun 和样本标志
internal static class Fmp4Samples
{
	public const uint VideoTrack = 1;
	public const uint AudioTrack = 2;
	public const uint SyncFlags = 0x0200_0000;
	public const uint NonSyncFlags = 0x0101_0000;

	public static byte[] Init(bool hasVideo = true, uint videoTrexFlags = NonSyncFlags, byte tkhdVersion = 0)
	{
		List<byte[]> moov = [];

		if (hasVideo)
		{
			moov.Add(Track(VideoTrack, "vide", tkhdVersion));
		}

		moov.Add(Track(AudioTrack, "soun", tkhdVersion));
		moov.Add(Box("mvex", [.. Trex(AudioTrack, SyncFlags), .. hasVideo ? Trex(VideoTrack, videoTrexFlags) : []]));

		return [.. Box("ftyp", "isom"u8.ToArray()), .. Box("moov", [.. moov.SelectMany(static box => box)])];
	}

	public static byte[] Fragment(bool isKeyFrame, SampleFlagsSource source = SampleFlagsSource.FirstSampleFlags, string marker = "")
	{
		uint flags = isKeyFrame ? SyncFlags : NonSyncFlags;

		// 音频轨排在视频轨前面，确认按轨道 ID 找的是视频
		byte[] audio = Box("traf", [.. Tfhd(AudioTrack, SyncFlags), .. Trun(0x04, 3, NonSyncFlags)]);
		byte[] video = source switch
		{
			SampleFlagsSource.FirstSampleFlags => Box("traf", [.. Tfhd(VideoTrack, NonSyncFlags), .. Trun(0x04 | 0x01, 2, flags)]),
			SampleFlagsSource.PerSampleFlags => Box("traf", [.. Tfhd(VideoTrack, NonSyncFlags), .. Trun(0x400 | 0x100 | 0x200, 2, flags)]),
			SampleFlagsSource.TrackFragmentDefault => Box("traf", [.. Tfhd(VideoTrack, flags), .. Trun(0, 2, 0)]),
			_ => Box("traf", [.. Tfhd(VideoTrack, null), .. Trun(0, 2, 0)])
		};

		return [.. Box("moof", [.. audio, .. video]), .. Box("mdat", Encoding.ASCII.GetBytes($"[{marker}]"))];
	}

	public static byte[] Box(string type, byte[] payload)
	{
		byte[] box = new byte[8 + payload.Length];
		BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
		Encoding.ASCII.GetBytes(type, box.AsSpan(4));
		payload.CopyTo(box.AsSpan(8));
		return box;
	}

	private static byte[] Track(uint trackId, string handler, byte tkhdVersion)
	{
		byte[] tkhd = new byte[tkhdVersion is 1 ? 36 : 24];
		tkhd[0] = tkhdVersion;
		BinaryPrimitives.WriteUInt32BigEndian(tkhd.AsSpan(tkhdVersion is 1 ? 20 : 12), trackId);

		byte[] hdlr = new byte[24];
		Encoding.ASCII.GetBytes(handler, hdlr.AsSpan(8));

		return Box("trak", [.. Box("tkhd", tkhd), .. Box("mdia", Box("hdlr", hdlr))]);
	}

	private static byte[] Trex(uint trackId, uint flags)
	{
		byte[] trex = new byte[24];
		BinaryPrimitives.WriteUInt32BigEndian(trex.AsSpan(4), trackId);
		BinaryPrimitives.WriteUInt32BigEndian(trex.AsSpan(20), flags);
		return Box("trex", trex);
	}

	// 带上 default_sample_duration，确认会跳过 default_sample_flags 前面的可选字段
	private static byte[] Tfhd(uint trackId, uint? defaultFlags)
	{
		uint flags = 0x08 | (defaultFlags is null ? 0u : 0x20);
		byte[] tfhd = new byte[defaultFlags is null ? 12 : 16];
		BinaryPrimitives.WriteUInt32BigEndian(tfhd, flags);
		BinaryPrimitives.WriteUInt32BigEndian(tfhd.AsSpan(4), trackId);
		BinaryPrimitives.WriteUInt32BigEndian(tfhd.AsSpan(8), 1500);

		if (defaultFlags is { } value)
		{
			BinaryPrimitives.WriteUInt32BigEndian(tfhd.AsSpan(12), value);
		}

		return Box("tfhd", tfhd);
	}

	private static byte[] Trun(uint flags, uint samples, uint sampleFlags)
	{
		List<byte> trun = [];
		Append(flags);
		Append(samples);

		if ((flags & 0x01) is not 0)
		{
			Append(0);
		}

		if ((flags & 0x04) is not 0)
		{
			Append(sampleFlags);
		}

		for (uint sample = 0; sample < samples; ++sample)
		{
			if ((flags & 0x100) is not 0)
			{
				Append(1500);
			}

			if ((flags & 0x200) is not 0)
			{
				Append(100);
			}

			if ((flags & 0x400) is not 0)
			{
				Append(sample is 0 ? sampleFlags : NonSyncFlags);
			}
		}

		return Box("trun", [.. trun]);

		void Append(uint value)
		{
			byte[] bytes = new byte[4];
			BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
			trun.AddRange(bytes);
		}
	}
}

public enum SampleFlagsSource
{
	FirstSampleFlags,
	PerSampleFlags,
	TrackFragmentDefault,
	TrackExtendsDefault
}
