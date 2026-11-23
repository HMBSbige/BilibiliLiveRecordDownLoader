using System.Collections.Specialized;
using System.ComponentModel;

namespace BilibiliLiveRecordDownLoader.ViewModels.Common;

// 热门房间每次刷新几十条弹幕，改筛选条件要重排上千条日志，逐条通知会让列表逐条重新布局
public sealed class RangeObservableCollection<T> : ObservableCollection<T>
{
	private List<T> ItemList => (List<T>)Items;

	public void AddRange(IReadOnlyList<T> items)
	{
		if (items.Count is 0)
		{
			return;
		}

		CheckReentrancy();
		int index = ItemList.Count;
		ItemList.AddRange(items);
		OnChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, items.ToArray(), index));
	}

	public void RemoveFirst(int count)
	{
		count = Math.Min(count, ItemList.Count);

		if (count <= 0)
		{
			return;
		}

		CheckReentrancy();
		List<T> removed = ItemList.GetRange(0, count);
		ItemList.RemoveRange(0, count);
		OnChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, removed, 0));
	}

	public void Reset(IEnumerable<T> items)
	{
		CheckReentrancy();
		ItemList.Clear();
		ItemList.AddRange(items);
		OnChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
	}

	private void OnChanged(NotifyCollectionChangedEventArgs e)
	{
		OnPropertyChanged(EventArgsCache.CountPropertyChanged);
		OnPropertyChanged(EventArgsCache.IndexerPropertyChanged);
		OnCollectionChanged(e);
	}
}

file static class EventArgsCache
{
	public static readonly PropertyChangedEventArgs CountPropertyChanged = new(nameof(ObservableCollection<>.Count));
	public static readonly PropertyChangedEventArgs IndexerPropertyChanged = new("Item[]");
}
