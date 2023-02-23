using BilibiliLiveRecordDownLoader.Http.Interfaces;
using BilibiliLiveRecordDownLoader.Shared.Abstractions;
using BilibiliLiveRecordDownLoader.Shared.Interfaces;
using System.Buffers;

namespace BilibiliLiveRecordDownLoader.Http.Clients;

public class HttpDownloader : ProgressBase, IDownloader, IHttpClient
{
	public Uri? Target { get; set; }

	public string? OutFileName { get; set; }

	public HttpClient Client { get; set; }

	public HttpDownloader(HttpClient client)
	{
		Client = client;
	}

	public async ValueTask DownloadAsync(CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrEmpty(OutFileName))
		{
			throw new InvalidOperationException(@"no output file path");
		}

		await using Stream remoteStream = await Client.GetStreamAsync(Target, cancellationToken);

		EnsureDirectory(OutFileName);
		await using FileStream fs = new(OutFileName, FileMode.Create, FileAccess.Write, FileShare.Read);

		using (CreateSpeedMonitor())
		{
			await CopyToWithProgressAsync(remoteStream, fs, cancellationToken);
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

	private async ValueTask CopyToWithProgressAsync(Stream from, Stream to, CancellationToken cancellationToken)
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
