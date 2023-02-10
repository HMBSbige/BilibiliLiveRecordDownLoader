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

	public async ValueTask DownloadAsync(CancellationToken token)
	{
		if (OutFileName is null or @"")
		{
			throw new ArgumentNullException(nameof(OutFileName));
		}

		if (_netStream is null)
		{
			await GetStreamAsync(token);
		}

		EnsureDirectory(OutFileName);
		await using FileStream fs = new(OutFileName, FileMode.Create, FileAccess.Write, FileShare.Read);

		using (CreateSpeedMonitor())
		{
			Pipe pipe = new(PipeOptions);
			Task task = pipe.Reader.CopyToAsync(fs, token);
			try
			{
				await CopyToWithProgressAsync(_netStream, pipe.Writer, token);
			}
			finally
			{
				await pipe.Writer.CompleteAsync();
				await task;
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
