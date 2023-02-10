using BilibiliLiveRecordDownLoader.FlvProcessor.Interfaces;
using BilibiliLiveRecordDownLoader.Services;
using BilibiliLiveRecordDownLoader.Utils;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Reactive.Linq;

namespace BilibiliLiveRecordDownLoader.Models.TaskViewModels;

public class FlvExtractTaskViewModel : TaskViewModel
{
	private readonly ILogger<FlvExtractTaskViewModel> _logger;
	private readonly CancellationTokenSource _cts = new();

	private readonly string _flv;

	public string? OutputVideo { get; set; }
	public string? OutputAudio { get; set; }

	public FlvExtractTaskViewModel(string flv)
	{
		_logger = DI.GetLogger<FlvExtractTaskViewModel>();
		_flv = flv;

		Description = $@"抽取 {flv}";
	}

	public override async Task StartAsync()
	{
		try
		{
			Status = @"正在抽取 FLV...";
			Progress = 0.0;

			await using var extractor = DI.GetRequiredService<IFlvExtractor>();
			extractor.OutputDir = Path.GetDirectoryName(_flv);

			using var ds = extractor.Status.DistinctUntilChanged().Subscribe(s => Status = s);

			using var d = extractor.CurrentSpeed.DistinctUntilChanged().Subscribe(speed => Speed = $@"{speed.ToHumanBytesString()}/s");

			using var dp = Observable.Interval(TimeSpan.FromSeconds(0.1))
				.DistinctUntilChanged()
				.Subscribe(_ =>
					// ReSharper disable once AccessToDisposedClosure
					Progress = extractor.Progress);

			await extractor.ExtractAsync(_flv, _cts.Token);

			OutputVideo = extractor.OutputVideo;
			OutputAudio = extractor.OutputAudio;

			Speed = string.Empty;
			Progress = 1.0;
		}
		catch (OperationCanceledException)
		{
			_logger.LogInformation($@"抽取 FLV 已取消：{_flv}");
			throw;
		}
		catch (Exception)
		{
			Status = @"出错";
			throw;
		}
	}

	public override void Stop()
	{
		_cts.Cancel();
	}
}
