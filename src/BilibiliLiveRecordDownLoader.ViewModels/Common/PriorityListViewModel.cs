namespace BilibiliLiveRecordDownLoader.ViewModels.Common;

public sealed partial class PriorityItemViewModel(Enum value, string label) : ReactiveObject
{
	public Enum Value { get; } = value;

	public string Label { get; } = label;

	[Reactive]
	public partial bool CanMoveUp { get; internal set; }

	[Reactive]
	public partial bool CanMoveDown { get; internal set; }
}

public sealed partial class PriorityListViewModel : ViewModelBase
{
	public ObservableCollection<PriorityItemViewModel> Items { get; } = [];

	[Reactive]
	public partial IReadOnlyList<Enum> Values { get; private set; } = [];

	public PriorityListViewModel()
	{
		MoveUpCommand.DisposeWith(Disposables);
		MoveDownCommand.DisposeWith(Disposables);
	}

	public static PriorityListViewModel Create<T>(IEnumerable<T> priority) where T : struct, Enum
	{
		PriorityListViewModel list = new();
		list.Reset(priority);
		return list;
	}

	public void Reset<T>(IEnumerable<T> priority) where T : struct, Enum
	{
		Items.Clear();

		foreach (T value in priority.Concat(Enum.GetValues<T>()).Distinct())
		{
			Items.Add(new PriorityItemViewModel(value, StreamLabels.Of(value)));
		}

		OnOrderChanged();
	}

	public IReadOnlyList<T> GetValues<T>() where T : struct, Enum
	{
		return [.. Values.OfType<T>()];
	}

	[ReactiveCommand]
	private void MoveUp(PriorityItemViewModel item)
	{
		Move(item, -1);
	}

	[ReactiveCommand]
	private void MoveDown(PriorityItemViewModel item)
	{
		Move(item, 1);
	}

	private void Move(PriorityItemViewModel item, int offset)
	{
		int index = Items.IndexOf(item);
		int target = index + offset;

		if (index < 0 || target < 0 || target >= Items.Count)
		{
			return;
		}

		Items.Move(index, target);
		OnOrderChanged();
	}

	private void OnOrderChanged()
	{
		for (int index = 0; index < Items.Count; ++index)
		{
			Items[index].CanMoveUp = index > 0;
			Items[index].CanMoveDown = index < Items.Count - 1;
		}

		Values = [.. Items.Select(static item => item.Value)];
	}
}
