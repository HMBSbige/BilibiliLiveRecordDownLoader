namespace BilibiliLiveRecordDownLoader.ViewModels.Common;

internal static class ReadOnlyListExtensions
{
	extension<T>(IReadOnlyList<T> list)
	{
		// 返回第一个让 isBefore 不成立的位置，都成立时为 Count；列表要按 isBefore 分好，成立的都在前面
		public int LowerBound(Func<T, bool> isBefore)
		{
			int low = 0;
			int high = list.Count;

			while (low < high)
			{
				int middle = low + high >>> 1;

				if (isBefore(list[middle]))
				{
					low = middle + 1;
				}
				else
				{
					high = middle;
				}
			}

			return low;
		}
	}
}
