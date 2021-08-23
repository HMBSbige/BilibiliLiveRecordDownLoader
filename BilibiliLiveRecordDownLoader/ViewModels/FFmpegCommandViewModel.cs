using BilibiliLiveRecordDownLoader.FFmpeg;
using BilibiliLiveRecordDownLoader.Models.TaskViewModels;
using BilibiliLiveRecordDownLoader.Services;
using BilibiliLiveRecordDownLoader.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Threading;
using Microsoft.WindowsAPICodePack.Dialogs;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using System;
using System.IO;
using System.Reactive;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace BilibiliLiveRecordDownLoader.ViewModels
{
	public class FFmpegCommandViewModel : ReactiveObject, IRoutableViewModel
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

		#region 属性

		[Reactive]
		public string FFmpegStatus { get; set; } = @"未知";

		[Reactive]
		public SolidColorBrush FFmpegStatusForeground { get; set; } = Constants.YellowBrush;

		[Reactive]
		public string CutInput { get; set; } = string.Empty;

		[Reactive]
		public string CutOutput { get; set; } = string.Empty;

		[Reactive]
		public string CutStartTime { get; set; } = @"00:00:00.000";

		[Reactive]
		public string CutEndTime { get; set; } = @"03:00:00.000";

		[Reactive]
		public string ConvertInput { get; set; } = string.Empty;

		[Reactive]
		public string ConvertOutput { get; set; } = string.Empty;

		[Reactive]
		public bool IsDelete { get; set; }

		[Reactive]
		public bool IsFlvFixConvert { get; set; } = true;

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

			this.WhenAnyValue(x => x.CutInput).Subscribe(_ => NewOutputFile());

			this.WhenAnyValue(x => x.ConvertInput).Subscribe(file =>
			{
				if (Path.GetExtension(file) == @".flv")
				{
					ConvertOutput = Path.ChangeExtension(file, @"mp4");
				}
			});
		}

		private async Task<bool> CheckFFmpegStatusAsync(CancellationToken token)
		{
			try
			{
				using var ffmpeg = DI.GetRequiredService<FFmpegCommand>();
				var version = await ffmpeg.GetVersionAsync(token);
				if (version is not null)
				{
					FFmpegStatus = $@"版本：{version}";
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

		private void NewOutputFile()
		{
			var filename = CutInput;
			if (!File.Exists(filename))
			{
				return;
			}

			var oldName = Path.ChangeExtension(filename, null);
			var extension = Path.GetExtension(filename);

			var match = Regex.Match(CutOutput, $@"^{Regex.Escape(oldName)}_(\d+){extension}$", RegexOptions.IgnoreCase);
			if (match.Groups.Count == 2 && ulong.TryParse(match.Groups[1].Value, out var l) && l < ulong.MaxValue)
			{
				CutOutput = Path.ChangeExtension($@"{oldName}_{l + 1}", extension);
				return;
			}

			for (var i = 1; i < 10000; ++i)
			{
				var newPath = Path.ChangeExtension($@"{oldName}_{i}", extension);
				if (newPath == CutOutput || File.Exists(newPath))
				{
					continue;
				}

				CutOutput = newPath;
				break;
			}
		}

		private void CutOpenFile()
		{
			var filename = GetOpenFileName();
			if (filename is null)
			{
				return;
			}

			CutInput = filename;
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
				_taskList.AddTaskAsync(task, Path.GetPathRoot(CutOutput) ?? string.Empty).Forget();

				NewOutputFile();
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

			ConvertVideoWithFixAsync(ConvertInput, ConvertOutput, IsDelete, IsFlvFixConvert).Forget();

			ConvertInput = string.Empty;
			ConvertOutput = string.Empty;
		}

		private async ValueTask ConvertVideoAsync(string input, string output, bool isDelete, string args)
		{
			var task = new FFmpegTaskViewModel(args);
			await _taskList.AddTaskAsync(task, Path.GetPathRoot(output) ?? string.Empty);
			if (isDelete)
			{
				FileUtils.DeleteWithoutException(input);
			}
		}

		private async ValueTask ConvertVideoWithFixAsync(string input, string output, bool isDelete, bool isFlvFixConvert)
		{
			try
			{
				string args;
				if (isFlvFixConvert)
				{
					var flv = new FlvExtractTaskViewModel(input);

					await _taskList.AddTaskAsync(flv, Path.GetPathRoot(output) ?? string.Empty);
					try
					{
						args = string.Format(Constants.FFmpegVideoAudioConvert, flv.OutputVideo, flv.OutputAudio, output);
						await ConvertVideoAsync(input, output, isDelete, args);
					}
					finally
					{
						FileUtils.DeleteWithoutException(flv.OutputVideo);
						FileUtils.DeleteWithoutException(flv.OutputAudio);
					}
				}
				else
				{
					args = string.Format(Constants.FFmpegCopyConvert, input, output);
					await ConvertVideoAsync(input, output, isDelete, args);
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
