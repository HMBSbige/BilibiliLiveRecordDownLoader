using BilibiliLiveRecordDownLoader.Http.Interfaces;
using BilibiliLiveRecordDownLoader.Shared.Abstractions;
using BilibiliLiveRecordDownLoader.Shared.Interfaces;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;

namespace BilibiliLiveRecordDownLoader.Http.Clients;

public class HttpDownloader : ProgressBase, IDownloader, IHttpClient
{
	public Uri? Target { get; set; }

	public string? OutFileName { get; set; }

	public HttpClient Client { get; set; }

	public PipeOptions PipeOptions { get; set; }

	private Stream? _netStream;

	public Task WriteToFileTask { get; private set; } = Task.CompletedTask;

	public bool WaitWriteToFile { get; set; } = true;

	public HttpDownloader(HttpClient client)
	{
		Client = client;
		PipeOptions = new PipeOptions(pauseWriterThreshold: 0);
	}

	[MemberNotNull(nameof(_netStream))]
	public async ValueTask GetStreamAsync(CancellationToken token)
	{
		_netStream = await Client.GetStreamAsync(Target, token);
	}

	public void CloseStream()
	{
		_netStream?.Dispose();
	}

	public async ValueTask DownloadAsync(CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrEmpty(OutFileName))
		{
			throw new OperationCanceledException(@"no output file path");
		}

		if (_netStream is null)
		{
			await GetStreamAsync(cancellationToken);
		}

		EnsureDirectory(OutFileName);
		Pipe pipe = new(PipeOptions);
		FileStream fs = new(OutFileName, FileMode.Create, FileAccess.Write, FileShare.Read);

		WriteToFileTask = pipe.Reader.CopyToAsync(fs, cancellationToken)
			.ContinueWith(_ => fs.Dispose(), default, TaskContinuationOptions.None, TaskScheduler.Default);
		try
		{
			using (CreateSpeedMonitor())
			{
				await CopyToWithProgressAsync(_netStream, pipe.Writer, cancellationToken);
			}
		}
		finally
		{
			await pipe.Writer.CompleteAsync();
			if (WaitWriteToFile)
			{
				await WriteToFileTask;
			}
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
}
