using BilibiliLiveRecordDownLoader.ViewModels;
using ReactiveUI;
using System.Reactive.Disposables;

namespace BilibiliLiveRecordDownLoader.Views;

public partial class TaskListView
{
	public TaskListView(TaskListViewModel viewModel)
	{
		InitializeComponent();
		ViewModel = viewModel;

		this.WhenActivated(d =>
		{
			this.OneWayBind(ViewModel, vm => vm.TaskList, v => v.TaskListDataGrid.ItemsSource).DisposeWith(d);

			var selectedItems = this.WhenAnyValue(v => v.TaskListDataGrid.SelectedItems);

			this.BindCommand(ViewModel,
				vm => vm.StopTaskCommand,
				v => v.StopTaskMenuItem,
				selectedItems).DisposeWith(d);
			this.BindCommand(ViewModel, vm => vm.ClearAllTasksCommand, v => v.RemoveTaskMenuItem).DisposeWith(d);
		});
	}
}
