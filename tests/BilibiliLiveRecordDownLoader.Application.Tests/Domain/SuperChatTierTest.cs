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
}
