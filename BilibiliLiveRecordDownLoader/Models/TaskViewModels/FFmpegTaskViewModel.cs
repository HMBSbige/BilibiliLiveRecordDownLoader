using BilibiliLiveRecordDownLoader.FFmpeg;
using Microsoft.Extensions.Logging;
using Splat;
using System;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace BilibiliLiveRecordDownLoader.Models.TaskViewModels
{
	public class FFmpegTaskViewModel : TaskViewModel
	{
		private readonly ILogger _logger;

		private readonly string _args;
		private readonly CancellationTokenSource _cts = new();

		public FFmpegTaskViewModel(string args)
		{
			_logger = Locator.Current.GetService<ILogger<FFmpegTaskViewModel>>();
			_args = args;

			Description = args;
			Speed = string.Empty;
		}

		public override async Task StartAsync()
		{
			try
			{
				_cts.Token.ThrowIfCancellationRequested();
				// Progress 和 Speed 懒得做
				Progress = 0.0;
				Status = @"启动中...";
				using (Observable.Interval(TimeSpan.FromSeconds(1)).Subscribe(_ =>
				{
					if (Progress < 0.99)
					{
						Progress += 0.01;
					}
				}))
				{
					using var ffmpeg = Locator.Current.GetService<FFmpegCommand>();
					using var messageMonitor = ffmpeg.MessageUpdated.Subscribe(str => Status = str);

					await ffmpeg.StartAsync(_args, _cts.Token);
				}
				Progress = 1.0;
			}
			catch (OperationCanceledException)
			{
				_logger.LogInformation($@"FFmpeg 已取消：{_args}");
			}
			catch (Exception ex)
			{
				Status = @"出错";
				_logger.LogError(ex, @"FFmpeg 出错");
			}
		}

		public override void Stop()
		{
			_cts.Cancel();
		}
	}
}
