using BilibiliApi.Clients;
using BilibiliLiveRecordDownLoader.FFmpeg;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reactive.Linq;

namespace BilibiliLiveRecordDownLoader.Services;

public class FFmpegLiveStreamRecorder : HttpLiveStreamRecorder
{
	public FFmpegLiveStreamRecorder(HttpClient client, ILogger<FFmpegLiveStreamRecorder> logger) : base(client, logger)
	{
	}

	public override async ValueTask DownloadAsync(string outFilePath, CancellationToken cancellationToken = default)
	{
		if (Source is null)
		{
			throw new InvalidOperationException(@"Do InitializeAsync first");
		}

		if (!Path.GetExtension(outFilePath).Equals(@".ts"))
		{
			outFilePath = Path.ChangeExtension(outFilePath, @".ts");
			Logger.LogInformation(@"FFmpeg 录制自动更改文件名=> {name}", outFilePath);
		}

		FileInfo file = new(outFilePath);
		file.Directory?.Create();

		FFmpegCommand ffmpeg = new();
		string args = $"""
-y -user_agent "{Client.DefaultRequestHeaders.UserAgent}" -headers "Referer: https://live.bilibili.com/" -rw_timeout {Client.Timeout.TotalMicroseconds} -i "{Source}" -c copy "{outFilePath}"
""";

		Task t = ffmpeg.StartAsync(args, cancellationToken);

		WriteToFileTask = t.ContinueWith(_ =>
		{
			ffmpeg.Dispose();
			return outFilePath;
		}, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Current);

		using (CreateFileSizeMonitor())
		{
			await t;
		}

		IDisposable CreateFileSizeMonitor()
		{
			Stopwatch sw = Stopwatch.StartNew();
			return Observable.Interval(TimeSpan.FromSeconds(1)).Subscribe(_ =>
			{
				file.Refresh();

				if (!file.Exists)
				{
					return;
				}

				long size = file.Length;
				long last = Interlocked.Read(ref FileSize);
				CurrentSpeedSubject.OnNext((size - last) / sw.Elapsed.TotalSeconds);
				sw.Restart();
				Interlocked.Exchange(ref FileSize, size);
			});
		}
	}
}
