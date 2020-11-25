using BilibiliLiveRecordDownLoader.FFmpeg;
using Microsoft.Extensions.Logging;
using Splat;
using System;
using System.Threading;
using System.Threading.Tasks;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace BilibiliLiveRecordDownLoader.Models.TaskViewModels
{
	public class FFmpegTaskViewModel : TaskViewModel
	{
		private readonly ILogger _logger;
		private readonly Config _config;

		private readonly string _args;
		private readonly CancellationTokenSource _cts = new();

		public FFmpegTaskViewModel(string args)
		{
			_logger = Locator.Current.GetService<ILogger<FFmpegTaskViewModel>>();
			_config = Locator.Current.GetService<Config>();
			_args = args;

			Description = args;
		}

		public override async Task StartAsync()
		{
			try
			{
				_cts.Token.ThrowIfCancellationRequested();

				using var ffmpeg = new FFmpegCommand();
				using var messageMonitor = ffmpeg.MessageUpdated.Subscribe(str => Status = str);

				await ffmpeg.StartAsync(_args, _cts.Token);

				throw new NotImplementedException();
			}
			catch (OperationCanceledException)
			{
				_logger.LogInformation($@"FFmpeg 已取消：{_args}");
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, @"FFmpeg 出错");
			}
			finally
			{
				Speed = string.Empty;
			}
		}

		public override void Stop()
		{
			_cts.Cancel();
		}
	}
}
