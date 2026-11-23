using BilibiliLiveRecordDownLoader.Application.Danmaku;
using System.Text;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Danmaku;

public sealed class DanmakuUidTest
{
	[Test]
	[Arguments("", 0x00000000u)]
	[Arguments("123456789", 0xCBF43926u)]
	public async Task Crc32MatchesTheStandardCheckValuesAsync(string input, uint expected)
	{
		await Assert.That(DanmakuUid.Crc32(Encoding.ASCII.GetBytes(input))).IsEqualTo(expected);
	}

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
