using BilibiliApi.Model;
using BilibiliLiveRecordDownLoader.Shared.Abstractions;
using BilibiliLiveRecordDownLoader.Shared.Utils;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;

namespace BilibiliApi.Clients;

public class HttpLiveStreamRecorder : ProgressBase, ILiveStreamRecorder
{
	private readonly ILogger<HttpLiveStreamRecorder> _logger;

	public HttpClient Client { get; set; }

	public long RoomId { get; set; }

	public Task<string>? WriteToFileTask { get; private set; }

	private static readonly PipeOptions PipeOptions = new(pauseWriterThreshold: 0);

	private Uri? _source;

	private IDisposable? _scope;

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

		_scope = _logger.BeginScope($@"{{{LoggerProperties.RoomIdPropertyName}}}", RoomId);
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
			.ContinueWith(_ =>
			{
				fs.Dispose();
				return filePath;
			}, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Current);

		try
		{
			using BlockingCollection<string> queue = new();
			using CancellationTokenSource getListCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			ValueTask task = GetListAsync(queue, getListCts.Token);

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
			catch (HttpRequestException ex)
			{
				_logger.LogError(@"尝试下载分片时服务器返回了 {statusCode}", ex.StatusCode);
			}
			finally
			{
				getListCts.Cancel();
			}

			await task;
		}
		finally
		{
			await pipe.Writer.CompleteAsync();
		}

		async ValueTask GetListAsync(BlockingCollection<string> queue, CancellationToken token)
		{
			try
			{
				CircleCollection<string> buffer = new(20);
				int count = default;
				int maxCount = (int)Math.Ceiling(Client.Timeout.TotalSeconds);

				using PeriodicTimer timer = new(TimeSpan.FromSeconds(1));
				do
				{
					await using Stream stream = await Client.GetStreamAsync(_source, token);

					M3U m3u8 = new(stream);

					foreach (string segment in m3u8.Segments)
					{
						if (buffer.AddIfNotContains(segment))
						{
							queue.Add(segment, token);
							count = default;
						}
					}

					if (m3u8.EndOfList)
					{
						_logger.LogInformation(@"收到结束信号直播流结束");
						break;
					}

					if (++count > maxCount)
					{
						_logger.LogInformation(@"连续 {count} 次未收到新分片，直播流可能结束", maxCount);
						break;
					}
				} while (await timer.WaitForNextTickAsync(token));
			}
			catch (HttpRequestException ex)
			{
				_logger.LogWarning(@"尝试下载 m3u8 时服务器返回了 {statusCode}", ex.StatusCode);
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
			Interlocked.Add(ref Last, buffer.LongLength);

			await pipe.Writer.WriteAsync(buffer, cancellationToken);
		}
	}

	public override async ValueTask DisposeAsync()
	{
		await base.DisposeAsync();

		_scope?.Dispose();
	}
}
