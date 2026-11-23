using BilibiliLiveRecordDownLoader.Application.Danmaku;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Danmaku;

public sealed class DanmakuUidTest
{
	// B 站的哈希不补前导零：33 的 CRC32 是 0x0A6216D9
	[Test]
	[Arguments(0L, "0")]
	[Arguments(123L, "884863d2")]
	[Arguments(33L, "a6216d9")]
	public async Task HashIsLowercaseHexOfTheDecimalUidAsync(long uid, string expected)
	{
		await Assert.That(DanmakuUid.Hash(uid)).IsEqualTo(expected);
	}
}
