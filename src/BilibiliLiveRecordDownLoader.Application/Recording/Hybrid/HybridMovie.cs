namespace BilibiliLiveRecordDownLoader.Application.Recording.Hybrid;

internal static class HybridMovie
{
	// 转换后盖住原来 moov 的 mdat 用 64 位大小的盒子头
	public const int MediaDataHeaderSize = 16;

	// 转换的最后一步一次改写 [0, 原 moov 开头 + MediaDataHeaderSize)；一个扇区的写入要么完整落盘要么没有，按最小的 512 字节扇区算
	public const int MaxHeadSize = 512;

	// 转换时写下的标记：放章节的 mdat 以它开头，头部备份放在以它为扩展类型的 uuid 盒子里
	public static ReadOnlySpan<byte> Signature => [0x0C, 0x91, 0x11, 0x39, 0xB1, 0x9F, 0x47, 0x79, 0x83, 0x3A, 0xCC, 0xB1, 0x43, 0xBE, 0xE1, 0x05];
}
