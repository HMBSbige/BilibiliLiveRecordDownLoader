namespace BilibiliLiveRecordDownLoader.FlvProcessor.Enums;

/// <summary>
/// 采样长度，1 bit
/// 压缩过的音频都是 16 bit
/// </summary>
public enum SoundSize : byte
{
	/// <summary>
	/// 8-bit samples
	/// </summary>
	Bit_8 = 0,

	/// <summary>
	/// 16-bit samples
	/// </summary>
	Bit_16 = 1
}
