namespace BilibiliLive.Enums;

public enum StreamEndReason
{
	EndOfStream,

	/// <summary>换了时间线或初始化分片，后续内容应写进新文件</summary>
	Discontinuity
}
