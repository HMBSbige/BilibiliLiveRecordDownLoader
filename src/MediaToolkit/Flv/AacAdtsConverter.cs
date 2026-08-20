using System.Buffers;

namespace MediaToolkit.Flv;

// FLV 里的 AAC 是裸帧，参数只在 AudioSpecificConfig 中出现一次，裸流播放需要给每帧加上 ADTS 头
internal sealed class AacAdtsConverter : IFlvTagConverter
{
	private const int SoundFormatAac = 10;
	private const byte PacketTypeSequenceHeader = 0;
	private const byte PacketTypeRaw = 1;

	// SoundFormat + SoundRate + SoundSize + SoundType、AACPacketType
	private const int AudioTagHeaderSize = 2;

	private const int AdtsHeaderSize = 7;
	private const int MaxAdtsFrameLength = 0x1FFF;

	private const int SbrObjectType = 5;
	private const int PsObjectType = 29;
	private const int ExplicitSampleRateIndex = 15;

	private AdtsConfiguration? _configuration;

	public void Convert(ReadOnlySpan<byte> data, IBufferWriter<byte> output)
	{
		if (data is not [var flags, var packetType, ..] || flags >> 4 is not SoundFormatAac)
		{
			return;
		}

		ReadOnlySpan<byte> body = data.Slice(AudioTagHeaderSize);

		switch (packetType)
		{
			case PacketTypeSequenceHeader:
			{
				_configuration = ParseAudioSpecificConfig(body);
				break;
			}
			case PacketTypeRaw when _configuration is { } configuration:
			{
				WriteFrame(configuration, body, output);
				break;
			}
		}
	}

	internal static AdtsConfiguration? ParseAudioSpecificConfig(ReadOnlySpan<byte> config)
	{
		BitReader reader = new(config);

		if (!reader.TryRead(5, out int objectType)
			|| !reader.TryRead(4, out int sampleRateIndex)
			|| sampleRateIndex is ExplicitSampleRateIndex
			|| !reader.TryRead(4, out int channelConfiguration))
		{
			return null;
		}

		// HE-AAC 的显式信令把核心层的编码类型放在扩展采样率之后，ADTS 只描述核心层，SBR/PS 由解码器自行发现
		if (objectType is SbrObjectType or PsObjectType)
		{
			if (!reader.TryRead(4, out int extensionSampleRateIndex)
				|| extensionSampleRateIndex is ExplicitSampleRateIndex && !reader.TryRead(24, out _)
				|| !reader.TryRead(5, out objectType))
			{
				return null;
			}
		}

		// ADTS 的 profile 只有 2 位，只能表示 Main、LC、SSR、LTP
		if (objectType is < 1 or > 4 || sampleRateIndex > 12 || channelConfiguration > 7)
		{
			return null;
		}

		return new AdtsConfiguration(objectType - 1, sampleRateIndex, channelConfiguration);
	}

	private static void WriteFrame(AdtsConfiguration configuration, ReadOnlySpan<byte> frame, IBufferWriter<byte> output)
	{
		int frameLength = AdtsHeaderSize + frame.Length;

		if (frame.IsEmpty || frameLength > MaxAdtsFrameLength)
		{
			return;
		}

		(int profile, int sampleRateIndex, int channelConfiguration) = configuration;
		Span<byte> destination = output.GetSpan(frameLength);

		// syncword 0xFFF、MPEG-4、layer 0、无 CRC
		destination[0] = 0xFF;
		destination[1] = 0xF1;
		destination[2] = (byte)(profile << 6 | sampleRateIndex << 2 | channelConfiguration >> 2);
		destination[3] = (byte)((channelConfiguration & 0x03) << 6 | frameLength >> 11);
		destination[4] = (byte)(frameLength >> 3);
		// buffer fullness 0x7FF 表示 VBR，每个 ADTS 帧只含一个 raw data block
		destination[5] = (byte)((frameLength & 0x07) << 5 | 0x1F);
		destination[6] = 0xFC;

		frame.CopyTo(destination.Slice(AdtsHeaderSize));
		output.Advance(frameLength);
	}

	internal readonly record struct AdtsConfiguration(int Profile, int SampleRateIndex, int ChannelConfiguration);
}
