using BilibiliApi.Model;
using BilibiliLiveRecordDownLoader.Shared.Abstractions;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;

namespace BilibiliApi.Clients;

public class HttpLiveStreamRecorder : ProgressBase, ILiveStreamRecorder
{
	public HttpClient Client { get; set; }

	public Task WriteToFileTask { get; private set; } = Task.CompletedTask;

	private static readonly PipeOptions PipeOptions = new(pauseWriterThreshold: 0);

	private Uri[] _source = Array.Empty<Uri>();

	public HttpLiveStreamRecorder(HttpClient client)
	{
		Client = client;
	}

	public async ValueTask InitializeAsync(IEnumerable<Uri> source, CancellationToken cancellationToken = default)
	{
		Uri[] result = (await source.Select(uri => Observable.FromAsync(ct => Test(uri, ct))
				.Catch<Uri?, HttpRequestException>(_ => Observable.Return<Uri?>(null))
				.Where(r => r is not null)
			)
			.Merge()
			.ToArray()
			.ToTask(cancellationToken))!;

		if (!result.Any())
		{
			throw new HttpRequestException(@"没有可用的直播地址");
		}

		_source = result;

		async Task<Uri> Test(Uri uri, CancellationToken ct)
		{
			await using Stream _ = await Client.GetStreamAsync(uri, ct);
			return uri;
		}
	}

	public async ValueTask DownloadAsync(string outFilePath, CancellationToken cancellationToken = default)
	{
		if (!_source.Any())
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
				string lastFile = string.Empty;

				using PeriodicTimer timer = new(TimeSpan.FromSeconds(1));
				do
				{
					int retry = 0;
					const int maxRetry = 3;
					try
					{
						await using Stream m3u8Stream = await Client.GetStreamAsync(_source.First(), cancellationToken);

						M3U m3u8 = new(m3u8Stream);

						IEnumerable<string> segments = GetSequenceExcept(m3u8.Segments, lastFile);

						foreach (string segment in segments)
						{
							queue.Add(segment, cancellationToken);
							lastFile = segment;
						}
					}
					catch (HttpRequestException) when (++retry < maxRetry)
					{

					}
				} while (await timer.WaitForNextTickAsync(cancellationToken));
			}
			finally
			{
				queue.CompleteAdding();
			}

			IEnumerable<string> GetSequenceExcept(IReadOnlyList<string> current, string last)
			{
				return current.Contains(last) ? current.SkipWhile(x => x != last).Skip(1) : current;
			}
		}

		async ValueTask CopySegmentToWithProgressAsync(string segment)
		{
			Exception? exception = null;

			foreach (Uri baseUri in _source)
			{
				if (!Uri.TryCreate(baseUri, segment, out Uri? uri))
				{
					continue;
				}

				try
				{
					byte[] buffer = await Client.GetByteArrayAsync(uri, cancellationToken);

					await pipe.Writer.WriteAsync(buffer, cancellationToken);
					ReportProgress(buffer.LongLength);

					return;
				}
				catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
				{
					exception = ex;
				}
			}

			if (exception is not null)
			{
				throw exception;
			}

			throw new FormatException(@"所有 Uri 格式错误");

			void ReportProgress(long length)
			{
				Interlocked.Add(ref Last, length);
			}
		}
	}
}
