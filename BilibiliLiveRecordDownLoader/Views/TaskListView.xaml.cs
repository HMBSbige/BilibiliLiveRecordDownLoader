using BilibiliLiveRecordDownLoader.ViewModels;
using ReactiveUI;
using Splat;
using System.Reactive.Disposables;

namespace BilibiliLiveRecordDownLoader.Views
{
	public partial class TaskListView
	{
		public TaskListView()
		{
			InitializeComponent();
			ViewModel = Locator.Current.GetService<TaskListViewModel>();

			this.WhenActivated(d =>
			{
				this.OneWayBind(ViewModel, vm => vm.TaskList, v => v.TaskListDataGrid.ItemsSource).DisposeWith(d);
				this.WhenAnyValue(v => v.TaskListDataGrid.SelectedItems)
						.BindTo(ViewModel, vm => vm.SelectedItems)
						.DisposeWith(d);
				this.BindCommand(ViewModel, vm => vm.StopTaskCommand, v => v.StopTaskMenuItem, vm => vm.SelectedItems).DisposeWith(d);
				this.BindCommand(ViewModel, vm => vm.ClearAllTasksCommand, v => v.RemoveTaskMenuItem).DisposeWith(d);
			});
		}
	}
}
