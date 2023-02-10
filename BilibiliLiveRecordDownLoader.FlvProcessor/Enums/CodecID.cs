namespace BilibiliLiveRecordDownLoader.FlvProcessor.Enums;

/// <summary>
/// 编码 ID，半个字节
/// </summary>
public enum CodecID : byte
{
	Rgb = 0,
	RunLength = 1,
	SorensonH263 = 2,
	ScreenVideo = 3,
	On2VP6 = 4,
	On2VP6WithAlphaChannel = 5,
	ScreenVideoV2 = 6,
	AVC = 7,
	ITUH263 = 8,
	MPEG4ASP = 9,
	HEVC = 12,
}
