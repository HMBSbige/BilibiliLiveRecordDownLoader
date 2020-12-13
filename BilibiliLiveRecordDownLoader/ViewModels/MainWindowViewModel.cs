using BilibiliLiveRecordDownLoader.Interfaces;
using BilibiliLiveRecordDownLoader.Models;
using BilibiliLiveRecordDownLoader.Models.TaskViewModels;
using BilibiliLiveRecordDownLoader.Services;
using BilibiliLiveRecordDownLoader.Utils;
using DynamicData;
using ReactiveUI;
using System.Linq;
using System.Reactive;
using System.Windows.Forms;

namespace BilibiliLiveRecordDownLoader.ViewModels
{
	public sealed class MainWindowViewModel : ReactiveObject, IScreen
	{
		#region Command

		public ReactiveCommand<Unit, Unit> ShowWindowCommand { get; }
		public ReactiveCommand<Unit, Unit> ExitCommand { get; }

		#endregion

		private readonly SourceList<TaskViewModel> _taskSourceList;
		private readonly SourceList<RoomStatus> _roomList;

		public RoutingState Router { get; } = new();

		public readonly Config Config;

		public MainWindowViewModel(
			SourceList<TaskViewModel> taskSourceList,
			Config config,
			SourceList<RoomStatus> roomList)
		{
			_taskSourceList = taskSourceList;
			Config = config;
			_roomList = roomList;

			ShowWindowCommand = ReactiveCommand.Create(ShowWindow);
			ExitCommand = ReactiveCommand.Create(Exit);
		}

		private void StopAllTask()
		{
			_taskSourceList.Items.ToList().ForEach(t => t.Stop());
			_roomList.Items.ToList().ForEach(room => room.Stop());
		}

		private static void ShowWindow()
		{
			DI.GetService<MainWindow>().ShowWindow();
		}

		private void Exit()
		{
			StopAllTask();

			DI.GetService<IConfigService>().Dispose();

			var window = DI.GetService<MainWindow>();
			window.CloseReason = CloseReason.ApplicationExitCall;
			window.Close();
		}
	}
}
