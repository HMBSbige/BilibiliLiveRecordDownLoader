namespace BilibiliLiveRecordDownLoader.FlvProcessor.Enums;

/// <summary>
/// https://en.wikipedia.org/wiki/Action_Message_Format#AMF0
/// https://www.adobe.com/content/dam/acom/en/devnet/flv/video_file_format_spec_v10_1.pdf
/// </summary>
public enum AMF0 : byte
{
	Number = 0x00,
	Boolean = 0x01,
	String = 0x02,
	Object = 0x03,
	MovieClip = 0x04,
	Null = 0x05,
	Undefined = 0x06,
	Reference = 0x07,
	ECMAArray = 0x08,
	ObjectEnd = 0x09,
	StrictArray = 0x0a,
	Date = 0x0b,
	LongString = 0x0c
}
