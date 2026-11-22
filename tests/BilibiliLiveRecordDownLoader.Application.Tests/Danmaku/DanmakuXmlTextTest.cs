using BilibiliLiveRecordDownLoader.Application.Danmaku;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Danmaku;

public sealed class DanmakuXmlTextTest
{
	// 范围外的值换算时也不能溢出
	[Test]
	[Arguments("922337203685.4775807", true)]
	[Arguments("-922337203685.4775808", true)]
	[Arguments("922337203685.4775808", false)]
	[Arguments("922337203685.4776", false)]
	[Arguments("-922337203685.4776", false)]
	[Arguments("1e30", false)]
	public async Task SecondsAreCheckedExactlyAtTheLimitsAsync(string value, bool expected)
	{
		bool parsed = DanmakuXmlText.TryParseSeconds(value, out TimeSpan result);

		using (Assert.Multiple())
		{
			await Assert.That(parsed).IsEqualTo(expected);
			await Assert.That(result.Ticks).IsEqualTo(expected ? (value.StartsWith('-') ? TimeSpan.MinValue.Ticks : TimeSpan.MaxValue.Ticks) : 0);
		}
	}

	[Test]
	public async Task SecondsKeepMillisecondsExactlyAsync()
	{
		bool parsed = DanmakuXmlText.TryParseSeconds("12.345", out TimeSpan result);

		using (Assert.Multiple())
		{
			await Assert.That(parsed).IsTrue();
			await Assert.That(result).IsEqualTo(TimeSpan.FromMilliseconds(12345));
		}
	}
}
