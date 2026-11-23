namespace BilibiliLiveRecordDownLoader.Application.Contracts.Danmaku;

// 沿用 B 站 SC 按价格分档的配色；MinPrice 是千分之一元，颜色是 RGB
// B 站原样的价格色在浅色底上几乎看不清，PriceColor 沿它的色相加深到对比度 4.5:1
// TextColor 取白色和 #333333 中在下半部分底色上对比度更高的；1000 元档两者都不到 4.5:1，不改底色时白色已是最好的
public readonly record struct SuperChatTier(long MinPrice, int Background, int BottomBackground, int PriceColor, int TextColor)
{
	private const int White = 0xFFFFFF;
	private const int DarkText = 0x333333;

	private static readonly SuperChatTier[] Tiers =
	[
		new(30_000, 0xEDF5FF, 0x2A60B2, 0x4270B8, White),
		new(50_000, 0xDBFFFD, 0x427D9E, 0x4B7793, White),
		new(100_000, 0xFFF1C5, 0xE2B52B, 0x856912, DarkText),
		new(500_000, 0xFFEAD2, 0xE09443, 0x9E5B1B, DarkText),
		new(1_000_000, 0xFFE7E4, 0xE54D4D, 0xD11F1F, White),
		new(2_000_000, 0xFFD8D8, 0xAB1A32, 0xAB3F52, White)
	];

	public static ReadOnlySpan<SuperChatTier> All => Tiers;

	public static SuperChatTier For(long price)
	{
		SuperChatTier tier = Tiers[0];

		foreach (SuperChatTier candidate in Tiers)
		{
			if (candidate.MinPrice > price)
			{
				break;
			}

			tier = candidate;
		}

		return tier;
	}
}
