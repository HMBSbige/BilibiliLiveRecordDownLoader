using BilibiliLiveRecordDownLoader.Http.Interfaces;
using BilibiliLiveRecordDownLoader.Shared.Abstractions;
using BilibiliLiveRecordDownLoader.Shared.Interfaces;
using System;
using System.Buffers;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace BilibiliLiveRecordDownLoader.Http.Clients
{
	public class HttpDownloader : ProgressBase, IDownloader, IHttpClient
	{
		public Uri? Target { get; set; }

		public string? OutFileName { get; set; }

		public HttpClient Client { get; set; }
		private Stream? _netStream;

		public HttpDownloader(HttpClient client)
		{
			Client = client;
		}

		public async Task GetStreamAsync(CancellationToken token)
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

			_netStream ??= await Client.GetStreamAsync(Target, token);
			EnsureDirectory(OutFileName);
			await using var fs = new FileStream(OutFileName, FileMode.Create, FileAccess.Write, FileShare.Read);

			using (CreateSpeedMonitor())
			{
				await CopyToAsyncWithProgress(_netStream, fs, token);
			}
		}

		private async Task CopyToAsyncWithProgress(Stream from, Stream to, CancellationToken token, int bufferSize = 81920)
		{
			using var memory = MemoryPool<byte>.Shared.Rent(bufferSize);
			while (true)
			{
				var length = await from.ReadAsync(memory.Memory, token);
				if (length != 0)
				{
					await to.WriteAsync(memory.Memory.Slice(0, length), token);
					ReportProgress(length);
				}
				else
				{
					break;
				}
			}
		}

		private void ReportProgress(long length)
		{
			Interlocked.Add(ref Last, length);
		}

		private static void EnsureDirectory(string path)
		{
			var dir = Path.GetDirectoryName(path);
			Directory.CreateDirectory(dir!);
		}
	}
}
