using BilibiliLiveRecordDownLoader.Http.Interfaces;
using BilibiliLiveRecordDownLoader.Shared.Abstracts;
using BilibiliLiveRecordDownLoader.Shared.Utils;
using System;
using System.Buffers;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace BilibiliLiveRecordDownLoader.Http.Clients
{
	public class HttpDownloader : ProgressBase, IDownloader
	{
		public Uri? Target { get; set; }

		public string? OutFileName { get; set; }

		private readonly HttpClient _httpClient;

		public HttpDownloader(TimeSpan timeout, string? cookie = null, string userAgent = Constants.ChromeUserAgent)
		{
			_httpClient = HttpClientUtils.BuildClientForBilibili(userAgent, cookie, timeout);
		}

		public HttpDownloader(HttpClient client)
		{
			_httpClient = client;
		}

		public async ValueTask DownloadAsync(CancellationToken token)
		{
			if (OutFileName is null or @"")
			{
				throw new ArgumentNullException(nameof(OutFileName));
			}

			var stream = await _httpClient.GetStreamAsync(Target, token);
			await using var fs = new FileStream(OutFileName, FileMode.Create, FileAccess.Write, FileShare.Read);

			using (CreateSpeedMonitor())
			{
				await CopyToAsyncWithProgress(stream, fs, token);
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

		public override async ValueTask DisposeAsync()
		{
			await base.DisposeAsync();

			_httpClient.Dispose();
		}
	}
}
