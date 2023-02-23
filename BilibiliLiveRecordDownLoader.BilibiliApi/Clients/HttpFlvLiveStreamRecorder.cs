using BilibiliLiveRecordDownLoader.Shared.Abstractions;
using System.Buffers;
using System.IO.Pipelines;

namespace BilibiliApi.Clients;

public class HttpFlvLiveStreamRecorder : ProgressBase, ILiveStreamRecorder
{
	public HttpClient Client { get; set; }

	public Uri? Source { get; set; }

	public string? OutFilePath { get; set; }

	public Task WriteToFileTask { get; private set; } = Task.CompletedTask;

	private static readonly PipeOptions PipeOptions = new(pauseWriterThreshold: 0);

	private Stream? _netStream;

	public HttpFlvLiveStreamRecorder(HttpClient client)
	{
		Client = client;
	}

	public async ValueTask InitAsync(CancellationToken cancellationToken = default)
	{
		_netStream = await Client.GetStreamAsync(Source, cancellationToken);
	}

	public async ValueTask DownloadAsync(CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrEmpty(OutFilePath))
		{
			throw new InvalidOperationException(@"No output file path");
		}

		if (_netStream is null)
		{
			throw new InvalidOperationException(@"Do InitAsync first");
		}

		string filePath = Path.ChangeExtension(OutFilePath, @".flv");
		await using Stream remoteStream = _netStream;

		Pipe pipe = new(PipeOptions);
		EnsureDirectory(filePath);
		FileStream fs = new(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);

		WriteToFileTask = pipe.Reader.CopyToAsync(fs, CancellationToken.None)
			.ContinueWith(_ => fs.Dispose(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Current);
		try
		{
			using (CreateSpeedMonitor())
			{
				await CopyToWithProgressAsync(remoteStream, pipe.Writer, cancellationToken);
			}
		}
		finally
		{
			await pipe.Writer.CompleteAsync();
		}

		static void EnsureDirectory(string path)
		{
			string? dir = Path.GetDirectoryName(path);
			if (dir is null)
			{
				return;
			}

			Directory.CreateDirectory(dir);
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
