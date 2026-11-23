using BilibiliLiveRecordDownLoader.Application.Danmaku;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Danmaku;

public sealed class DanmakuXmlTextTest
{
	// 改写偏移时不能丢一毫秒；范围外的值换算时也不能溢出
	[Test]
	[Arguments("12.345", true, 123_450_000L)]
	[Arguments("922337203685.4775807", true, long.MaxValue)]
	[Arguments("-922337203685.4775808", true, long.MinValue)]
	[Arguments("922337203685.4775808", false, 0L)]
	[Arguments("-922337203685.4776", false, 0L)]
	[Arguments("1e30", false, 0L)]
	public async Task SecondsAreParsedExactlyUpToTheLimitsAsync(string value, bool expected, long ticks)
	{
		bool parsed = DanmakuXmlText.TryParseSeconds(value, out TimeSpan result);

		await Assert.That((parsed, result.Ticks)).IsEqualTo((expected, ticks));
	}
}
