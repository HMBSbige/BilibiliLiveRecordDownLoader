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
	protected readonly ILogger<HttpLiveStreamRecorder> Logger;

	public HttpClient Client { get; set; }

	public long RoomId { get; set; }

	public Task<string>? WriteToFileTask { get; protected set; }

	private static readonly PipeOptions PipeOptions = new(pauseWriterThreshold: 0);

	protected Uri? Source;

	private IDisposable? _scope;

	public HttpLiveStreamRecorder(HttpClient client, ILogger<HttpLiveStreamRecorder> logger)
	{
		Client = client;
		Logger = logger;
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

		Source = result ?? throw new HttpRequestException(@"没有可用的直播地址");

		_scope = Logger.BeginScope($@"{{{LoggerProperties.RoomIdPropertyName}}}", RoomId);
		Logger.LogInformation(@"选择直播地址：{uri}", Source);

		async Task<Uri> Test(Uri uri, CancellationToken ct)
		{
			await using Stream _ = await Client.GetStreamAsync(uri, ct);
			return uri;
		}
	}

	public virtual async ValueTask DownloadAsync(string outFilePath, CancellationToken cancellationToken = default)
	{
		if (Source is null)
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
				Logger.LogError(@"尝试下载分片时服务器返回了 {statusCode}", ex.StatusCode);
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

				using PeriodicTimer timer = new(TimeSpan.FromSeconds(1));
				do
				{
					await using Stream stream = await Client.GetStreamAsync(Source, token);

					M3U m3u8 = new(stream);

					foreach (string segment in m3u8.Segments)
					{
						if (buffer.AddIfNotContains(segment))
						{
							queue.Add(segment, token);
						}
					}

					if (m3u8.EndOfList)
					{
						Logger.LogInformation(@"收到结束信号直播流结束");
						break;
					}
				} while (await timer.WaitForNextTickAsync(token));
			}
			catch (HttpRequestException ex)
			{
				Logger.LogWarning(@"尝试下载 m3u8 时服务器返回了 {statusCode}", ex.StatusCode);
			}
			finally
			{
				queue.CompleteAdding();
			}
		}

		async ValueTask CopySegmentToWithProgressAsync(string segment)
		{
			if (!Uri.TryCreate(Source, segment, out Uri? uri))
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

		GC.SuppressFinalize(this);
	}
}
