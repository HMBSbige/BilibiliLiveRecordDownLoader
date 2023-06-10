using BilibiliLiveRecordDownLoader.Shared.Abstractions;
using BilibiliLiveRecordDownLoader.Shared.Utils;
using Microsoft.Extensions.Logging;
using System.Buffers;
using System.IO.Pipelines;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;

namespace BilibiliApi.Clients;

public class HttpFlvLiveStreamRecorder : ProgressBase, ILiveStreamRecorder
{
	public HttpClient Client { get; set; }

	public long RoomId { get; set; }

	public Task<string>? WriteToFileTask { get; private set; }

	private static readonly PipeOptions PipeOptions = new(pauseWriterThreshold: 0);

	private Stream? _netStream;

	protected readonly ILogger<HttpFlvLiveStreamRecorder> Logger;
	private IDisposable? _scope;

	public HttpFlvLiveStreamRecorder(HttpClient client, ILogger<HttpFlvLiveStreamRecorder> logger)
	{
		Client = client;
		Logger = logger;
	}

	public async ValueTask InitializeAsync(IEnumerable<Uri> source, CancellationToken cancellationToken = default)
	{
		Uri result = await source.Select(uri => Observable.FromAsync(ct => Test(uri, ct))
				.Catch<Uri?, HttpRequestException>(_ => Observable.Return<Uri?>(null))
				.Where(r => r is not null)
			)
			.Merge()
			.FirstOrDefaultAsync()
			.ToTask(cancellationToken) ?? throw new HttpRequestException(@"没有可用的直播地址");

		_scope = Logger.BeginScope($@"{{{LoggerProperties.RoomIdPropertyName}}}", RoomId);
		Logger.LogInformation(@"选择直播地址：{uri}", result);

		_netStream = await Client.GetStreamAsync(result, cancellationToken);

		async Task<Uri> Test(Uri uri, CancellationToken ct)
		{
			await using Stream _ = await Client.GetStreamAsync(uri, ct);
			return uri;
		}
	}

	public async ValueTask DownloadAsync(string outFilePath, CancellationToken cancellationToken = default)
	{
		if (_netStream is null)
		{
			throw new InvalidOperationException(@"Do InitializeAsync first");
		}

		FileInfo file = new(outFilePath);

		Pipe pipe = new(PipeOptions);
		file.Directory?.Create();
		FileStream fs = file.Open(FileMode.Create, FileAccess.Write, FileShare.Read);

		WriteToFileTask = pipe.Reader.CopyToAsync(fs, CancellationToken.None)
			.ContinueWith(_ =>
			{
				fs.Dispose();
				return outFilePath;
			}, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Current);

		try
		{
			using (CreateSpeedMonitor())
			{
				await using Stream remoteStream = _netStream;
				await CopyToWithProgressAsync(remoteStream, pipe.Writer, cancellationToken);
			}
		}
		finally
		{
			await pipe.Writer.CompleteAsync();
		}
	}

	private async ValueTask CopyToWithProgressAsync(Stream from, PipeWriter to, CancellationToken cancellationToken)
	{
		const int bufferSize = 81920;
		using IMemoryOwner<byte> memory = MemoryPool<byte>.Shared.Rent(bufferSize);
		while (true)
		{
			int length = await from.ReadAsync(memory.Memory, cancellationToken);
			if (length is not 0)
			{
				await to.WriteAsync(memory.Memory[..length], cancellationToken);
				ReportProgress(length);
			}
			else
			{
				break;
			}
		}

		void ReportProgress(long length)
		{
			Interlocked.Add(ref Last, length);
		}
	}

	public override async ValueTask DisposeAsync()
	{
		await base.DisposeAsync();

		_scope?.Dispose();

		if (_netStream is not null)
		{
			await _netStream.DisposeAsync();
		}

		GC.SuppressFinalize(this);
	}
}
