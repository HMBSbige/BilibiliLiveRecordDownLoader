namespace BilibiliLiveRecordDownLoader.ViewModels.Rooms;

// 移动的房间放到返回的房间之前，null 表示末尾
internal static class RoomReorder
{
	public static long? Top(IReadOnlyList<long> order, IReadOnlySet<long> moving)
	{
		return At(order, moving, 0);
	}

	public static long? Up(IReadOnlyList<long> order, IReadOnlySet<long> moving)
	{
		for (int index = 0; index < order.Count; ++index)
		{
			if (moving.Contains(order[index]))
			{
				return index > 0 ? order[index - 1] : Top(order, moving);
			}
		}

		return null;
	}

	// 越过最后一个选中房间之后的那个房间
	public static long? Down(IReadOnlyList<long> order, IReadOnlySet<long> moving)
	{
		for (int index = order.Count - 1; index >= 0; --index)
		{
			if (moving.Contains(order[index]))
			{
				return index + 2 < order.Count ? order[index + 2] : null;
			}
		}

		return null;
	}

	public static long? At(IReadOnlyList<long> order, IReadOnlySet<long> moving, int index)
	{
		for (int current = Math.Max(index, 0); current < order.Count; ++current)
		{
			if (!moving.Contains(order[current]))
			{
				return order[current];
			}
		}

		return null;
	}
}
