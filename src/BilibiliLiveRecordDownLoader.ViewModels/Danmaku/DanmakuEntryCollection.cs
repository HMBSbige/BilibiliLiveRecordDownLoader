using System.Collections.Specialized;
using System.ComponentModel;

namespace BilibiliLiveRecordDownLoader.ViewModels.Danmaku;

// 热门房间每次刷新几十条，逐条通知会让列表逐条重新布局
public sealed class DanmakuEntryCollection : ObservableCollection<DanmakuEntryViewModel>
{
	private static readonly PropertyChangedEventArgs CountChanged = new(nameof(Count));
	private static readonly PropertyChangedEventArgs IndexerChanged = new("Item[]");

	public void AddRange(IReadOnlyList<DanmakuEntryViewModel> items)
	{
		if (items.Count is 0)
		{
			return;
		}

		CheckReentrancy();
		int index = Items.Count;

		foreach (DanmakuEntryViewModel item in items)
		{
			Items.Add(item);
		}

		OnPropertyChanged(CountChanged);
		OnPropertyChanged(IndexerChanged);
		OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, items.ToArray(), index));
	}

	public void RemoveFirst(int count)
	{
		if (count <= 0)
		{
			return;
		}

		CheckReentrancy();
		DanmakuEntryViewModel[] removed = [.. Items.Take(count)];

		for (int index = 0; index < removed.Length; ++index)
		{
			Items.RemoveAt(0);
		}

		OnPropertyChanged(CountChanged);
		OnPropertyChanged(IndexerChanged);
		OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, removed, 0));
	}

	public void Reset(IEnumerable<DanmakuEntryViewModel> items)
	{
		CheckReentrancy();
		Items.Clear();

		foreach (DanmakuEntryViewModel item in items)
		{
			Items.Add(item);
		}

		OnPropertyChanged(CountChanged);
		OnPropertyChanged(IndexerChanged);
		OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
	}
}
