namespace BilibiliLiveRecordDownLoader.Application.Rooms;

internal static class RoomOrder
{
	public static bool MoveBefore<T>(List<T> items, Func<T, long> getRoomId, IReadOnlySet<long> roomIds, long? before)
	{
		T[] moving = [.. items.Where(item => roomIds.Contains(getRoomId(item)))];

		if (moving.Length is 0)
		{
			return false;
		}

		List<T> rest = [.. items.Where(item => !roomIds.Contains(getRoomId(item)))];
		int insertAt = rest.Count;

		if (before is { } anchor)
		{
			int index = items.FindIndex(item => getRoomId(item) == anchor);

			if (index < 0)
			{
				return false;
			}

			int next = items.FindIndex(index, item => !roomIds.Contains(getRoomId(item)));
			insertAt = next < 0 ? rest.Count : rest.FindIndex(item => getRoomId(item) == getRoomId(items[next]));
		}

		rest.InsertRange(insertAt, moving);

		if (rest.Select(getRoomId).SequenceEqual(items.Select(getRoomId)))
		{
			return false;
		}

		items.Clear();
		items.AddRange(rest);
		return true;
	}
}
