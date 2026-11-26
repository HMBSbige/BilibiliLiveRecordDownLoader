namespace BilibiliLiveRecordDownLoader.Application.Recording.Hybrid;

// 从文件里读出的盒子头，位置都按文件开头算
internal readonly record struct BoxHeader(long Start, long Size, int HeaderSize, uint Type)
{
	public long End => Start + Size;

	public long ContentStart => Start + HeaderSize;

	public long ContentSize => Size - HeaderSize;
}
