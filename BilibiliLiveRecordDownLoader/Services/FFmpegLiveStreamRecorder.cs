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

		string filePath = Path.ChangeExtension(outFilePath, @".mp4");
		FileInfo file = new(filePath);
		file.Directory?.Create();

		FFmpegCommand ffmpeg = new();
		string args = $"""-rw_timeout {Client.Timeout.TotalMicroseconds} -i "{Source}" -c copy -f mp4 -movflags frag_keyframe+empty_moov+delay_moov "{filePath}" -y""";

		Task t = ffmpeg.StartAsync(args, cancellationToken);

		WriteToFileTask = t.ContinueWith(_ =>
		{
			ffmpeg.Dispose();
			return filePath;
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
