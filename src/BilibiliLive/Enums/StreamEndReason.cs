namespace BilibiliLive.Enums;

public enum StreamEndReason
{
	EndOfStream,

	/// <summary>流换了新的时间线或初始化分片继续，后续内容应写进新文件</summary>
	Discontinuity
}
