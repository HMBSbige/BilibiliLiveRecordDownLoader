using BilibiliLiveRecordDownLoader.FFmpeg;
using BilibiliLiveRecordDownLoader.Models.TaskViewModels;
using BilibiliLiveRecordDownLoader.Services;
using BilibiliLiveRecordDownLoader.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Threading;
using Microsoft.Win32;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using System.IO;
using System.Reactive;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace BilibiliLiveRecordDownLoader.ViewModels;

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
	public bool IsFlvFixConvert { get; set; }

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

		this.WhenAnyValue(x => x.CutInput).Subscribe(NewOutputFile);

		this.WhenAnyValue(x => x.ConvertInput).Subscribe(file =>
		{
			ConvertOutput = Path.GetExtension(file).ToLowerInvariant() is @".flv" or @".mkv" or @".ts" ? Path.ChangeExtension(file, @".mp4") : string.Empty;
		});
	}

	private async Task<bool> CheckFFmpegStatusAsync(CancellationToken token)
	{
		try
		{
			using FFmpegCommand ffmpeg = DI.GetRequiredService<FFmpegCommand>();
			string? version = await ffmpeg.GetVersionAsync(token);
			if (version is not null)
			{
				FFmpegStatus = $@"版本：{version}";
				FFmpegStatusForeground = Constants.NormalBlueBrush;
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

	private void NewOutputFile(string filename)
	{
		if (!File.Exists(filename))
		{
			return;
		}

		string oldName = Path.ChangeExtension(filename, null);
		string extension = Path.GetExtension(filename);

		Match match = Regex.Match(CutOutput, $@"^{Regex.Escape(oldName)}_(\d+){extension}$", RegexOptions.IgnoreCase);
		if (match.Groups.Count == 2 && ulong.TryParse(match.Groups[1].Value, out ulong l) && l < ulong.MaxValue)
		{
			CutOutput = Path.ChangeExtension($@"{oldName}_{l + 1}", extension);
			return;
		}

		for (int i = 1; i < 10000; ++i)
		{
			string newPath = Path.ChangeExtension($@"{oldName}_{i}", extension);
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
		string? filename = GetOpenFileName();
		if (filename is null)
		{
			return;
		}

		CutInput = filename;
	}

	private void CutSaveFile()
	{
		string? filename = GetSaveFileName(CutOutput);
		if (filename is null)
		{
			return;
		}

		CutOutput = filename;
	}

	private void ConvertOpenFile()
	{
		string? filename = GetOpenFileName();
		if (filename is null)
		{
			return;
		}

		ConvertInput = filename;
	}

	private void ConvertSaveFile()
	{
		string? filename = GetSaveFileName(ConvertOutput);
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
			string args = string.Format(Constants.FFmpegSplitTo, CutStartTime, CutEndTime, CutInput, CutOutput);
			FFmpegTaskViewModel task = new(args);
			_taskList.AddTaskAsync(task, Path.GetPathRoot(CutOutput) ?? string.Empty).Forget();

			NewOutputFile(CutInput);
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
		FFmpegTaskViewModel task = new(args);
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
			if (isFlvFixConvert
				&& Path.GetExtension(input).Equals(@".flv", StringComparison.OrdinalIgnoreCase)
				&& Path.GetExtension(output).Equals(@".mp4", StringComparison.OrdinalIgnoreCase)
				)
			{
				FlvExtractTaskViewModel flv = new(input);

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
		OpenFileDialog dlg = new()
		{
			Filter = Constants.VideoFilter
		};
		if (dlg.ShowDialog() is true)
		{
			return dlg.FileName;
		}
		return null;
	}

	private static string? GetSaveFileName(string defaultPath)
	{
		SaveFileDialog dlg = new()
		{
			DefaultExt = Path.GetExtension(defaultPath),
			FileName = Path.GetFileName(defaultPath),
			InitialDirectory = Path.GetDirectoryName(defaultPath),
			Filter = Constants.VideoFilter
		};
		if (dlg.ShowDialog() is true)
		{
			return dlg.FileName;
		}
		return null;
	}
}
