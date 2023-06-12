using BilibiliLiveRecordDownLoader.Shared.Abstractions;
using BilibiliLiveRecordDownLoader.Shared.Utils;
using Microsoft.Extensions.Logging;
using System.Buffers;
using System.IO.Pipelines;

namespace BilibiliApi.Clients;

public class HttpFlvLiveStreamRecorder : ProgressBase, ILiveStreamRecorder
{
	public HttpClient Client { get; set; }

	public long RoomId { get; set; }

	public Task<string>? WriteToFileTask { get; private set; }

	private const long CacheLength = 64 * 1024 * 1024;
	private static readonly PipeOptions PipeOptions = new(pauseWriterThreshold: CacheLength, resumeWriterThreshold: CacheLength / 2);

	private Stream? _netStream;

	protected readonly ILogger<HttpFlvLiveStreamRecorder> Logger;
	private IDisposable? _scope;

	public HttpFlvLiveStreamRecorder(HttpClient client, ILogger<HttpFlvLiveStreamRecorder> logger)
	{
		Client = client;
		Logger = logger;
	}

	public async ValueTask InitializeAsync(Uri source, CancellationToken cancellationToken = default)
	{
		_scope = Logger.BeginScope($@"{{{LoggerProperties.RoomIdPropertyName}}}", RoomId);

		_netStream = await Client.GetStreamAsync(source, cancellationToken);
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
