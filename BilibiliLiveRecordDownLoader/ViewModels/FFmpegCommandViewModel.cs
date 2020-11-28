using BilibiliLiveRecordDownLoader.FFmpeg;
using BilibiliLiveRecordDownLoader.Models.TaskViewModels;
using BilibiliLiveRecordDownLoader.Shared.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAPICodePack.Dialogs;
using ReactiveUI;
using System;
using System.IO;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using Constants = BilibiliLiveRecordDownLoader.Utils.Constants;

namespace BilibiliLiveRecordDownLoader.ViewModels
{
#pragma warning disable CS8612
	public class FFmpegCommandViewModel : ReactiveObject, IRoutableViewModel
#pragma warning restore CS8612
	{
		public string UrlPathSegment => @"FFmpegCommand";
		public IScreen HostScreen { get; }

		#region Command

		public ReactiveCommand<Unit, bool> CheckFFmpegStatusCommand { get; }
		public ReactiveCommand<Unit, Unit> CutOpenFileCommand { get; }
		public ReactiveCommand<Unit, Unit> CutSaveFileCommand { get; }
		public ReactiveCommand<Unit, Unit> CutCommand { get; }
		public ReactiveCommand<Unit, Unit> ConvertOpenFileCommand { get; }
		public ReactiveCommand<Unit, Unit> ConvertSaveFileCommand { get; }
		public ReactiveCommand<Unit, Unit> ConvertCommand { get; }

		#endregion

		#region 字段

		private string _ffmpegStatus = @"未知";
		private SolidColorBrush _ffmpegStatusForeground = Constants.YellowBrush;

		private string _cutInput = string.Empty;
		private string _cutOutput = string.Empty;
		private string _cutStartTime = @"00:00:00.000";
		private string _cutEndTime = @"03:00:00.000";

		private string _convertInput = string.Empty;
		private string _convertOutput = string.Empty;
		private bool _isDelete = false;

		#endregion

		#region 属性

		public string FFmpegStatus
		{
			get => _ffmpegStatus;
			set => this.RaiseAndSetIfChanged(ref _ffmpegStatus, value);
		}

		public SolidColorBrush FFmpegStatusForeground
		{
			get => _ffmpegStatusForeground;
			set => this.RaiseAndSetIfChanged(ref _ffmpegStatusForeground, value);
		}

		public string CutInput
		{
			get => _cutInput;
			set => this.RaiseAndSetIfChanged(ref _cutInput, value);
		}

		public string CutOutput
		{
			get => _cutOutput;
			set => this.RaiseAndSetIfChanged(ref _cutOutput, value);
		}

		public string CutStartTime
		{
			get => _cutStartTime;
			set => this.RaiseAndSetIfChanged(ref _cutStartTime, value);
		}

		public string CutEndTime
		{
			get => _cutEndTime;
			set => this.RaiseAndSetIfChanged(ref _cutEndTime, value);
		}

		public string ConvertInput
		{
			get => _convertInput;
			set => this.RaiseAndSetIfChanged(ref _convertInput, value);
		}

		public string ConvertOutput
		{
			get => _convertOutput;
			set => this.RaiseAndSetIfChanged(ref _convertOutput, value);
		}

		public bool IsDelete
		{
			get => _isDelete;
			set => this.RaiseAndSetIfChanged(ref _isDelete, value);
		}

		#endregion

		private readonly ILogger _logger;
		private readonly TaskListViewModel _taskList;

		public FFmpegCommandViewModel(
				IScreen hostScreen,
				ILogger<FFmpegCommandViewModel> logger,
				TaskListViewModel taskList)
		{
			HostScreen = hostScreen;
			_logger = logger;
			_taskList = taskList;

			CheckFFmpegStatusCommand = ReactiveCommand.CreateFromTask(CheckFFmpegStatusAsync);
			CutOpenFileCommand = ReactiveCommand.Create(CutOpenFile);
			CutSaveFileCommand = ReactiveCommand.Create(CutSaveFile);
			CutCommand = ReactiveCommand.Create(CreateCutVideoTask);
			ConvertOpenFileCommand = ReactiveCommand.Create(ConvertOpenFile);
			ConvertSaveFileCommand = ReactiveCommand.Create(ConvertSaveFile);
			ConvertCommand = ReactiveCommand.Create(CreateConvertVideoTask);
		}

		private async Task<bool> CheckFFmpegStatusAsync(CancellationToken token)
		{
			try
			{
				using var ffmpeg = new FFmpegCommand();
				if (await ffmpeg.VerifyAsync(token))
				{
					FFmpegStatus = @"成功";
					FFmpegStatusForeground = Constants.GreenBrush;
					return true;
				}

				FFmpegStatus = @"FFmpeg 运行失败！请将 FFmpeg.exe 放至程序根目录或系统路径后单击重新检查状态";
				FFmpegStatusForeground = Constants.RedBrush;
			}
			catch (Exception ex)
			{
				FFmpegStatus = @"错误";
				FFmpegStatusForeground = Constants.RedBrush;
				_logger.LogError(ex, FFmpegStatus);
			}
			return false;
		}

		private void CutOpenFile()
		{
			var filename = GetOpenFileName();
			if (filename is null)
			{
				return;
			}

			CutInput = filename;

			var oldName = Path.ChangeExtension(filename, null);
			var extension = Path.GetExtension(filename);
			for (var i = 1; i < int.MaxValue; ++i)
			{
				var newPath = Path.ChangeExtension($@"{oldName}_{i}", extension);
				if (File.Exists(newPath))
				{
					continue;
				}

				CutOutput = newPath;
				break;
			}
		}

		private void CutSaveFile()
		{
			var filename = GetSaveFileName(CutOutput);
			if (filename is null)
			{
				return;
			}

			CutOutput = filename;
		}

		private void ConvertOpenFile()
		{
			var filename = GetOpenFileName();
			if (filename is null)
			{
				return;
			}

			ConvertInput = filename;

			if (Path.GetExtension(filename) == @".flv")
			{
				ConvertOutput = Path.ChangeExtension(filename, @"mp4");
			}
		}

		private void ConvertSaveFile()
		{
			var filename = GetSaveFileName(ConvertOutput);
			if (filename is null)
			{
				return;
			}

			ConvertOutput = filename;
		}

		private void CreateCutVideoTask()
		{
			try
			{
				if (string.IsNullOrWhiteSpace(CutInput) || string.IsNullOrWhiteSpace(CutOutput))
				{
					return;
				}
				var args = string.Format(Constants.FFmpegSplitTo, CutStartTime, CutEndTime, CutInput, CutOutput);
				var task = new FFmpegTaskViewModel(args);
				_taskList.AddTaskAsync(task, Path.GetPathRoot(CutOutput) ?? string.Empty).NoWarning();

				CutInput = string.Empty;
				CutOutput = string.Empty;
				CutStartTime = CutEndTime;
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, @"创建剪辑任务时发生错误");
			}
		}

		private void CreateConvertVideoTask()
		{
			if (string.IsNullOrWhiteSpace(ConvertInput) || string.IsNullOrWhiteSpace(ConvertOutput))
			{
				return;
			}

			ConvertVideoAsync(ConvertInput, ConvertOutput, IsDelete).NoWarning();

			ConvertInput = string.Empty;
			ConvertOutput = string.Empty;
		}

		private async Task ConvertVideoAsync(string input, string output, bool isDelete)
		{
			try
			{
				var args = string.Format(Constants.FFmpegCopyConvert, input, output);
				var task = new FFmpegTaskViewModel(args);
				await _taskList.AddTaskAsync(task, Path.GetPathRoot(output) ?? string.Empty);
				if (isDelete)
				{
					File.Delete(input);
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, @"转封装任务时发生错误");
			}
		}

		private static string? GetOpenFileName()
		{
			var dlg = new CommonOpenFileDialog
			{
				IsFolderPicker = false,
				Multiselect = false,
				Title = @"打开",
				AddToMostRecentlyUsedList = false,
				EnsurePathExists = true,
				NavigateToShortcut = true,
				Filters =
				{
					Constants.VideoFilter,
					Constants.AllFilter
				}
			};
			if (dlg.ShowDialog() == CommonFileDialogResult.Ok)
			{
				return dlg.FileName;
			}
			return null;
		}

		private static string? GetSaveFileName(string defaultPath)
		{
			var dlg = new CommonSaveFileDialog
			{
				Title = @"另存为",
				AddToMostRecentlyUsedList = false,
				NavigateToShortcut = true,
				DefaultFileName = Path.GetFileName(defaultPath),
				DefaultExtension = Path.GetExtension(defaultPath),
				DefaultDirectory = Path.GetDirectoryName(defaultPath),
				Filters =
				{
					Constants.VideoFilter,
					Constants.AllFilter
				}
			};
			if (dlg.ShowDialog() == CommonFileDialogResult.Ok)
			{
				return dlg.FileName;
			}
			return null;
		}
	}
}
