using BilibiliLiveRecordDownLoader.Application.Danmaku;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Danmaku;

public sealed class DanmakuUidTest
{
	[Test]
	[Arguments("", 0x00000000u)]
	[Arguments("a", 0xE8B7BE43u)]
	[Arguments("123456789", 0xCBF43926u)]
	[Arguments("The quick brown fox jumps over the lazy dog", 0x414FA339u)]
	public async Task Crc32MatchesTheStandardCheckValuesAsync(string input, uint expected)
	{
		await Assert.That(DanmakuUid.Crc32(System.Text.Encoding.ASCII.GetBytes(input))).IsEqualTo(expected);
	}

	[Test]
	[Arguments(0L, "0")]
	[Arguments(123L, "884863d2")]
	public async Task HashIsLowercaseHexOfTheDecimalUidAsync(long uid, string expected)
	{
		await Assert.That(DanmakuUid.Hash(uid)).IsEqualTo(expected);
	}

	// B 站的哈希不补前导零
	[Test]
	public async Task HashHasNoLeadingZerosAsync()
	{
		long uid = Enumerable.Range(1, 1000).First(static i => DanmakuUid.Crc32(System.Text.Encoding.ASCII.GetBytes(i.ToString(System.Globalization.CultureInfo.InvariantCulture))) < 0x10000000u);

		await Assert.That(DanmakuUid.Hash(uid).Length).IsLessThan(8);
	}
}
