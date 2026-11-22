namespace BilibiliLiveRecordDownLoader.Application.Tests.Domain;

public sealed class SuperChatTierTest
{
	[Test]
	[Arguments(1_000L, 30_000L)]
	[Arguments(30_000L, 30_000L)]
	[Arguments(49_990L, 30_000L)]
	[Arguments(50_000L, 50_000L)]
	[Arguments(999_000L, 500_000L)]
	[Arguments(2_000_000L, 2_000_000L)]
	[Arguments(5_000_000L, 2_000_000L)]
	public async Task PriceFallsIntoTheHighestTierNotAboveItAsync(long price, long expectedMinPrice)
	{
		await Assert.That(SuperChatTier.For(price).MinPrice).IsEqualTo(expectedMinPrice);
	}

	[Test]
	public async Task TiersAreSortedByPriceAsync()
	{
		long[] prices = SuperChatTier.All.ToArray().Select(static tier => tier.MinPrice).ToArray();

		await Assert.That(prices.SequenceEqual(prices.Order())).IsTrue();
	}

	[Test]
	public async Task ColorsKeepTextLegibleOnTheTierBackgroundsAsync()
	{
		SuperChatTier[] tiers = SuperChatTier.All.ToArray();

		using (Assert.Multiple())
		{
			foreach (SuperChatTier tier in tiers)
			{
				int better = Contrast(0xFFFFFF, tier.BottomBackground) >= Contrast(0x333333, tier.BottomBackground) ? 0xFFFFFF : 0x333333;

				await Assert.That(Contrast(tier.PriceColor, tier.Background)).IsGreaterThanOrEqualTo(4.5);
				await Assert.That(tier.TextColor).IsEqualTo(better);
			}
		}
	}

	// WCAG 2 的对比度
	private static double Contrast(int foreground, int background)
	{
		double first = Luminance(foreground);
		double second = Luminance(background);

		return (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);

		static double Luminance(int rgb)
		{
			return 0.2126 * Channel(rgb >> 16) + 0.7152 * Channel(rgb >> 8) + 0.0722 * Channel(rgb);
		}

		static double Channel(int value)
		{
			double channel = (value & 0xFF) / 255d;
			return channel <= 0.03928 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
		}
	}
}
