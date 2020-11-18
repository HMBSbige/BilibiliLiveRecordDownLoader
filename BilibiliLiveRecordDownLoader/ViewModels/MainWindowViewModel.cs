using BilibiliLiveRecordDownLoader.Interfaces;
using BilibiliLiveRecordDownLoader.Utils;
using BilibiliLiveRecordDownLoader.ViewModels.TaskViewModels;
using DynamicData;
using ReactiveUI;
using Splat;
using System.Linq;
using System.Reactive;
using System.Windows.Forms;

namespace BilibiliLiveRecordDownLoader.ViewModels
{
	public sealed class MainWindowViewModel : ReactiveObject
	{
		#region Command

		public ReactiveCommand<Unit, Unit> ShowWindowCommand { get; }
		public ReactiveCommand<Unit, Unit> ExitCommand { get; }

		#endregion

		private readonly SourceList<TaskViewModel> _taskSourceList;

		public IScreen HostScreen { get; }

		public MainWindowViewModel(
			SourceList<TaskViewModel> taskSourceList,
			IScreen screen)
		{
			_taskSourceList = taskSourceList;
			HostScreen = screen;

			ShowWindowCommand = ReactiveCommand.Create(ShowWindow);
			ExitCommand = ReactiveCommand.Create(Exit);
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

			Locator.Current.GetService<IConfigService>().Dispose();

			var window = Locator.Current.GetService<MainWindow>();
			window.CloseReason = CloseReason.ApplicationExitCall;
			window.Close();
		}
	}
}
