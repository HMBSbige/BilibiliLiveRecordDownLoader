using BilibiliLiveRecordDownLoader.Enums;
using BilibiliLiveRecordDownLoader.Interfaces;
using BilibiliLiveRecordDownLoader.Models;
using BilibiliLiveRecordDownLoader.Models.TaskViewModels;
using BilibiliLiveRecordDownLoader.Services;
using BilibiliLiveRecordDownLoader.Utils;
using BilibiliLiveRecordDownLoader.Views.Dialogs;
using DynamicData;
using ModernWpf.Controls;
using ReactiveUI;
using System.Reactive;
using System.Windows.Forms;

namespace BilibiliLiveRecordDownLoader.ViewModels;

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
		ExitCommand = ReactiveCommand.CreateFromTask(ExitAsync);
	}

	private void StopAllTask()
	{
		_taskSourceList.Items.ToList().ForEach(t => t.Stop());
		_roomList.Items.ToList().ForEach(room => room.Stop());
	}

	private static void ShowWindow()
	{
		DI.GetRequiredService<MainWindow>().ShowWindow();
	}

	private bool HasTaskRunning()
	{
		return _taskSourceList.Items.ToList().Any(t => !t.Status.Contains(@"完成")) || _roomList.Items.ToList().Any(room => room.RecordStatus != RecordStatus.未录制);
	}

	private async Task ExitAsync()
	{
		if (HasTaskRunning())
		{
			ShowWindow();
			using DisposableContentDialog dialog = new();
			dialog.Title = @"退出程序？";
			dialog.Content = @"还有任务正在进行";
			dialog.PrimaryButtonText = @"确定";
			dialog.SecondaryButtonText = @"取消";
			dialog.DefaultButton = ContentDialogButton.Primary;
			if (await dialog.SafeShowAsync(10, ContentDialogResult.Primary) != ContentDialogResult.Primary)
			{
				return;
			}
		}

		StopAllTask();

		DI.GetRequiredService<IConfigService>().Dispose();

		MainWindow window = DI.GetRequiredService<MainWindow>();
		window.CloseReason = CloseReason.ApplicationExitCall;
		window.Close();
	}
}
