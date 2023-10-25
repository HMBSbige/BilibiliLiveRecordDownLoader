using BilibiliLiveRecordDownLoader.Models.TaskViewModels;
using BilibiliLiveRecordDownLoader.Views.Dialogs;
using DynamicData;
using Microsoft.Extensions.Logging;
using ModernWpf.Controls;
using Punchclock;
using ReactiveUI;
using System.Collections;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;

namespace BilibiliLiveRecordDownLoader.ViewModels;

public class TaskListViewModel : ReactiveObject, IRoutableViewModel
{
	public string UrlPathSegment => @"TaskList";
	public IScreen HostScreen { get; }

	#region Command

	public ReactiveCommand<object?, Unit> StopTaskCommand { get; }
	public ReactiveCommand<Unit, Unit> ClearAllTasksCommand { get; }

	#endregion

	private readonly ILogger _logger;
	private readonly SourceList<TaskViewModel> _taskSourceList;
	private readonly OperationQueue _taskQueue;

	public readonly ReadOnlyObservableCollection<TaskViewModel> TaskList;

	public TaskListViewModel(
		IScreen hostScreen,
		ILogger<TaskListViewModel> logger,
		SourceList<TaskViewModel> taskSourceList,
		OperationQueue taskQueue)
	{
		HostScreen = hostScreen;
		_logger = logger;
		_taskSourceList = taskSourceList;
		_taskQueue = taskQueue;

		_taskSourceList.Connect()
			.ObserveOn(RxApp.MainThreadScheduler)
			.Bind(out TaskList)
			.DisposeMany()
			.Subscribe();

		StopTaskCommand = ReactiveCommand.CreateFromObservable<object?, Unit>(StopTask);
		ClearAllTasksCommand = ReactiveCommand.CreateFromTask(ClearAllTasksAsync);
	}

	private IObservable<Unit> StopTask(object? info)
	{
		return Observable.Start(() =>
		{
			try
			{
				if (info is not IList { Count: > 0 } list)
				{
					return;
				}

				foreach (var item in list)
				{
					if (item is not TaskViewModel task)
					{
						continue;
					}

					RemoveTask(task);
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, @"停止任务出错");
			}
		});
	}

	private async Task ClearAllTasksAsync()
	{
		try
		{
			if (_taskSourceList.Count == 0)
			{
				return;
			}

			using DisposableContentDialog dialog = new();
			dialog.Title = @"确定清空所有任务？";
			dialog.Content = @"将会停止所有任务并清空列表";
			dialog.PrimaryButtonText = @"确定";
			dialog.CloseButtonText = @"取消";
			dialog.DefaultButton = ContentDialogButton.Close;
			if (await dialog.SafeShowAsync() == ContentDialogResult.Primary)
			{
				_taskSourceList.Items.ToList().ForEach(RemoveTask);
			}
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, @"停止任务出错");
		}
	}

	public async Task AddTaskAsync(TaskViewModel task, string key, int priority = 1)
	{
		if (_taskSourceList.Items.Any(x => x.Description == task.Description))
		{
			_logger.LogWarning(@"已跳过重复任务：{0}", task.Description);
			return;
		}

		_taskSourceList.Add(task);
		await _taskQueue.Enqueue(priority, key, task.StartAsync);
	}

	public void RemoveTask(TaskViewModel task)
	{
		task.Stop();
		_taskSourceList.Remove(task);
	}
}
