using BilibiliApi.Model;
using BilibiliLiveRecordDownLoader.Shared.Abstractions;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;

namespace BilibiliApi.Clients;

public class HttpLiveStreamRecorder : ProgressBase, ILiveStreamRecorder
{
	private readonly ILogger<HttpLiveStreamRecorder> _logger;

	public HttpClient Client { get; set; }

	public Task WriteToFileTask { get; private set; } = Task.CompletedTask;

	private static readonly PipeOptions PipeOptions = new(pauseWriterThreshold: 0);

	private Uri? _source;

	public HttpLiveStreamRecorder(HttpClient client, ILogger<HttpLiveStreamRecorder> logger)
	{
		Client = client;
		_logger = logger;
	}

	public async ValueTask InitializeAsync(IEnumerable<Uri> source, CancellationToken cancellationToken = default)
	{
		Uri? result = await source.Select(uri => Observable.FromAsync(ct => Test(uri, ct))
				.Catch<Uri?, HttpRequestException>(_ => Observable.Return<Uri?>(null))
				.Where(r => r is not null)
			)
			.Merge()
			.FirstOrDefaultAsync()
			.ToTask(cancellationToken);

		_source = result ?? throw new HttpRequestException(@"没有可用的直播地址");

		_logger.LogInformation(@"选择直播地址：{uri}", _source);

		async Task<Uri> Test(Uri uri, CancellationToken ct)
		{
			await using Stream _ = await Client.GetStreamAsync(uri, ct);
			return uri;
		}
	}

	public async ValueTask DownloadAsync(string outFilePath, CancellationToken cancellationToken = default)
	{
		if (_source is null)
		{
			throw new InvalidOperationException(@"Do InitializeAsync first");
		}

		string filePath = Path.ChangeExtension(outFilePath, @".ts");
		FileInfo file = new(filePath);

		Pipe pipe = new(PipeOptions);
		file.Directory?.Create();
		FileStream fs = file.Open(FileMode.Create, FileAccess.Write, FileShare.Read);

		WriteToFileTask = pipe.Reader.CopyToAsync(fs, CancellationToken.None)
			.ContinueWith(_ => fs.Dispose(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Current);

		using BlockingCollection<string> queue = new();
		ValueTask _ = GetListAsync();

		try
		{
			using (CreateSpeedMonitor())
			{
				foreach (string segment in queue.GetConsumingEnumerable(cancellationToken))
				{
					await CopySegmentToWithProgressAsync(segment);
				}
			}
		}
		finally
		{
			await pipe.Writer.CompleteAsync();
		}

		[SuppressMessage("ReSharper", "AccessToDisposedClosure")]
		async ValueTask GetListAsync()
		{
			try
			{
				CircleCollection<string> buffer = new(20);

				using PeriodicTimer timer = new(TimeSpan.FromSeconds(1));
				do
				{
					await using Stream m3u8Stream = await Client.GetStreamAsync(_source, cancellationToken);

					M3U m3u8 = new(m3u8Stream);

					foreach (string segment in m3u8.Segments)
					{
						if (buffer.AddIfNotContains(segment))
						{
							queue.Add(segment, cancellationToken);
						}
					}
				} while (await timer.WaitForNextTickAsync(cancellationToken));
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, @"处理 m3u8 时发生错误({uri})", _source);
			}
			finally
			{
				queue.CompleteAdding();
			}
		}

		async ValueTask CopySegmentToWithProgressAsync(string segment)
		{
			if (!Uri.TryCreate(_source, segment, out Uri? uri))
			{
				throw new FormatException(@"Uri 格式错误");
			}

			byte[] buffer = await Client.GetByteArrayAsync(uri, cancellationToken);

			await pipe.Writer.WriteAsync(buffer, cancellationToken);
			Interlocked.Add(ref Last, buffer.LongLength);
		}
	}
}
