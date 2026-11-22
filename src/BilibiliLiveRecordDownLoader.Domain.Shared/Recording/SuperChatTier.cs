namespace BilibiliLiveRecordDownLoader.Domain.Shared.Recording;

// 沿用 B 站 SC 按价格分档的配色，观众一眼就能认出档位：上半部分浅色底放昵称和价格，下半部分深色底放正文
// MinPrice 是千分之一元；颜色是 RGB
public readonly record struct SuperChatTier(long MinPrice, int Background, int PriceColor, int BottomBackground)
{
	private static readonly SuperChatTier[] Tiers =
	[
		new(30_000, 0xEDF5FF, 0x7497CD, 0x2A60B2),
		new(50_000, 0xDBFFFD, 0x7DA4BD, 0x427D9E),
		new(100_000, 0xFFF1C5, 0xECCF75, 0xE2B52B),
		new(500_000, 0xFFEAD2, 0xE8AF79, 0xE09443),
		new(1_000_000, 0xFFE7E4, 0xEE8B8B, 0xE54D4D),
		new(2_000_000, 0xFFD8D8, 0xC86A7A, 0xAB1A32)
	];

	public static ReadOnlySpan<SuperChatTier> All => Tiers;

	// 取不超过价格的最高档；低于最低档时按最低档
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
