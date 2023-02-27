using BilibiliLiveRecordDownLoader.Shared.Abstractions;
using System.Buffers;
using System.IO.Pipelines;

namespace BilibiliApi.Clients;

public class HttpFlvLiveStreamRecorder : ProgressBase, ILiveStreamRecorder
{
	public HttpClient Client { get; set; }

	public long RoomId { get; set; }

	public Task<string>? WriteToFileTask { get; private set; }

	private static readonly PipeOptions PipeOptions = new(pauseWriterThreshold: 0);

	private Stream? _netStream;

	public HttpFlvLiveStreamRecorder(HttpClient client)
	{
		Client = client;
	}

	public async ValueTask InitializeAsync(IEnumerable<Uri> source, CancellationToken cancellationToken = default)
	{
		_netStream = await Client.GetStreamAsync(source.First(), cancellationToken);
	}

	public async ValueTask DownloadAsync(string outFilePath, CancellationToken cancellationToken = default)
	{
		if (_netStream is null)
		{
			throw new InvalidOperationException(@"Do InitializeAsync first");
		}

		string filePath = Path.ChangeExtension(outFilePath, @".flv");
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
		if (_netStream is not null)
		{
			await _netStream.DisposeAsync();
		}
	}
}
