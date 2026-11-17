namespace BilibiliLiveRecordDownLoader.ViewModels.Common;

public sealed record PriorityItemViewModel(Enum Value, string Label);

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

		// 未列出的选项排在最后，界面上仍可调整
		foreach (T value in priority.Concat(Enum.GetValues<T>()).Distinct())
		{
			Items.Add(new PriorityItemViewModel(value, StreamLabels.Of(value)));
		}

		Values = [.. Items.Select(static item => item.Value)];
	}

	public List<T> ToList<T>() where T : struct, Enum
	{
		return [.. Items.Select(static item => item.Value).OfType<T>()];
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
		Values = [.. Items.Select(static item => item.Value)];
	}
}
