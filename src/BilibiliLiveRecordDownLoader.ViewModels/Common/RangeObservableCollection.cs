using System.Collections.Specialized;
using System.ComponentModel;

namespace BilibiliLiveRecordDownLoader.ViewModels.Common;

// 热门房间每次刷新几十条弹幕，改筛选条件要重排上千条日志，逐条通知会让列表逐条重新布局
public sealed class RangeObservableCollection<T> : ObservableCollection<T>
{
	private static readonly PropertyChangedEventArgs CountChanged = new(nameof(Count));
	private static readonly PropertyChangedEventArgs IndexerChanged = new("Item[]");

	public void AddRange(IReadOnlyList<T> items)
	{
		if (items.Count is 0)
		{
			return;
		}

		CheckReentrancy();
		int index = Items.Count;

		foreach (T item in items)
		{
			Items.Add(item);
		}

		OnChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, items.ToArray(), index));
	}

	public void RemoveFirst(int count)
	{
		if (count <= 0)
		{
			return;
		}

		CheckReentrancy();
		T[] removed = [.. Items.Take(count)];

		for (int index = 0; index < removed.Length; ++index)
		{
			Items.RemoveAt(0);
		}

		OnChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, removed, 0));
	}

	public void Reset(IEnumerable<T> items)
	{
		CheckReentrancy();
		Items.Clear();

		foreach (T item in items)
		{
			Items.Add(item);
		}

		OnChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
	}

	private void OnChanged(NotifyCollectionChangedEventArgs e)
	{
		OnPropertyChanged(CountChanged);
		OnPropertyChanged(IndexerChanged);
		OnCollectionChanged(e);
	}
}
