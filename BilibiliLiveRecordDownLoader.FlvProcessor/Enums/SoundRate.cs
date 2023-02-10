namespace BilibiliLiveRecordDownLoader.FlvProcessor.Enums;

/// <summary>
/// 采样率，2 bit
/// 对于 AAC 总是 44-kHz
/// </summary>
public enum SoundRate : byte
{
	/// <summary>
	/// 5.5-kHz
	/// </summary>
	kHz_5 = 0,

	/// <summary>
	/// 11-kHz
	/// </summary>
	kHz_11 = 1,

	/// <summary>
	/// 22-kHz
	/// </summary>
	kHz_22 = 2,

	/// <summary>
	/// 44-kHz
	/// </summary>
	kHz_44 = 3
}
