namespace BilibiliLiveRecordDownLoader.FlvProcessor.Enums;

/// <summary>
/// 音频格式，半字节
/// </summary>
public enum SoundFormat : byte
{
	/// <summary>
	/// Linear PCM, platform endian
	/// </summary>
	LinearPCM = 0,

	ADPCM = 1,
	MP3 = 2,

	/// <summary>
	/// Linear PCM, little endian
	/// </summary>
	LinearPCMLE = 3,

	/// <summary>
	/// Nellymoser 16-kHz mono
	/// </summary>
	Nellymoser16 = 4,

	/// <summary>
	/// Nellymoser 8-kHz mono
	/// </summary>
	Nellymoser8 = 5,

	Nellymoser = 6,

	/// <summary>
	/// G.711 A-law logarithmic PCM
	/// </summary>
	G711A = 7,

	/// <summary>
	/// G.711 mu-law logarithmic PCM
	/// </summary>
	G711Mu = 8,

	Reserved = 9,
	AAC = 10,
	Speex = 11,

	/// <summary>
	/// MP3 8-Khz
	/// </summary>
	MP3_8 = 14,

	/// <summary>
	/// Device-specific sound
	/// </summary>
	DeviceSpecific = 15
}
