using BilibiliLiveRecordDownLoader.Utils;
using BilibiliLiveRecordDownLoader.ViewModels.TaskViewModels;
using DynamicData;
using Microsoft.Extensions.Logging;
using ModernWpf.Controls;
using ReactiveUI;
using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace BilibiliLiveRecordDownLoader.ViewModels
{
#pragma warning disable CS8612
	public class TaskListViewModel : ReactiveObject, IRoutableViewModel
#pragma warning restore CS8612
	{
		public string UrlPathSegment => @"TaskList";
		public IScreen HostScreen { get; }

		#region Command

		public ReactiveCommand<object?, Unit> StopTaskCommand { get; }
		public ReactiveCommand<Unit, Unit> ClearAllTasksCommand { get; }

		#endregion

		private readonly ILogger _logger;
		private readonly SourceList<TaskViewModel> _taskSourceList;

		public readonly ReadOnlyObservableCollection<TaskViewModel> TaskList;

		public TaskListViewModel(
			IScreen hostScreen,
			ILogger<TaskListViewModel> logger,
			SourceList<TaskViewModel> taskSourceList)
		{
			HostScreen = hostScreen;
			_logger = logger;
			_taskSourceList = taskSourceList;

			_taskSourceList.Connect()
					.ObserveOnDispatcher()
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
					if (info is IList { Count: > 0 } list)
					{
						foreach (var item in list)
						{
							if (item is TaskViewModel task)
							{
								task.Stop();
								_taskSourceList.Remove(task);
							}
						}
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

				using var dialog = new DisposableContentDialog
				{
					Title = @"确定清空所有任务？",
					Content = @"将会停止所有任务并清空列表",
					PrimaryButtonText = @"确定",
					CloseButtonText = @"取消",
					DefaultButton = ContentDialogButton.Primary
				};
				if (await dialog.ShowAsync() == ContentDialogResult.Primary)
				{
					_taskSourceList.Items.ToList().ForEach(task =>
					{
						task.Stop();
						_taskSourceList.Remove(task);
					});
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, @"停止任务出错");
			}
		}
	}
}
