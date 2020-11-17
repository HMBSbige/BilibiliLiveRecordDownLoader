using BilibiliLiveRecordDownLoader.Interfaces;
using BilibiliLiveRecordDownLoader.Utils;
using BilibiliLiveRecordDownLoader.ViewModels.TaskViewModels;
using DynamicData;
using ReactiveUI;
using Splat;
using System;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Windows.Forms;

namespace BilibiliLiveRecordDownLoader.ViewModels
{
	public sealed class MainWindowViewModel : ReactiveObject, IDisposable
	{
		#region Monitor

		private readonly IDisposable _diskMonitor;

		#endregion

		#region Command

		public ReactiveCommand<Unit, Unit> ShowWindowCommand { get; }
		public ReactiveCommand<Unit, Unit> ExitCommand { get; }

		#endregion

		private readonly IConfigService _configService;
		private readonly SourceList<TaskViewModel> _taskSourceList;
		private readonly GlobalViewModel _global;

		public MainWindowViewModel(
			IConfigService configService,
			SourceList<TaskViewModel> taskSourceList,
			GlobalViewModel global)
		{
			_configService = configService;
			_taskSourceList = taskSourceList;
			_global = global;

			_diskMonitor = Observable.Interval(TimeSpan.FromSeconds(1), RxApp.MainThreadScheduler).Subscribe(GetDiskUsage);

			ShowWindowCommand = ReactiveCommand.Create(ShowWindow);
			ExitCommand = ReactiveCommand.Create(Exit);
		}

		private void GetDiskUsage(long _)
		{
			var (availableFreeSpace, totalSize) = Utils.Utils.GetDiskUsage(_configService.Config.MainDir);
			if (totalSize != 0)
			{
				_global.DiskUsageProgressBarText = $@"已使用 {Utils.Utils.CountSize(totalSize - availableFreeSpace)}/{Utils.Utils.CountSize(totalSize)} 剩余 {Utils.Utils.CountSize(availableFreeSpace)}";
				var percentage = (totalSize - availableFreeSpace) / (double)totalSize;
				_global.DiskUsageProgressBarValue = percentage * 100;
			}
			else
			{
				_global.DiskUsageProgressBarText = string.Empty;
				_global.DiskUsageProgressBarValue = 0;
			}
		}

		private void StopAllTask()
		{
			_taskSourceList.Items.ToList().ForEach(t => t.Stop());
		}

		private static void ShowWindow()
		{
			Locator.Current.GetService<MainWindow>().ShowWindow();
		}

		private void Exit()
		{
			StopAllTask();
			var window = Locator.Current.GetService<MainWindow>();
			window.CloseReason = CloseReason.ApplicationExitCall;
			window.Close();
		}

		public void Dispose()
		{
			_diskMonitor.Dispose();
			_configService.Dispose();
		}
	}
}
