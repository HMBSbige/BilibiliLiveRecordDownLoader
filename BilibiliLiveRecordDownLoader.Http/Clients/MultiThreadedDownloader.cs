using BilibiliLiveRecordDownLoader.Http.Interfaces;
using BilibiliLiveRecordDownLoader.Http.Models;
using BilibiliLiveRecordDownLoader.Shared.Abstracts;
using BilibiliLiveRecordDownLoader.Shared.HttpPolicy;
using BilibiliLiveRecordDownLoader.Shared.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using Punchclock;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks;

namespace BilibiliLiveRecordDownLoader.Http.Clients
{
	public class MultiThreadedDownloader : ProgressBase, IDownloader
	{
		private readonly ILogger _logger;

		public Uri? Target { get; set; }

		public string? OutFileName { get; set; }

		/// <summary>
		/// 线程数
		/// </summary>
		public ushort Threads { get; set; } = 8;

		/// <summary>
		/// 临时文件夹
		/// </summary>
		public string TempDir { get; set; } = Path.GetTempPath();

		private readonly ObjectPool<HttpClient> _httpClientPool;

		public MultiThreadedDownloader(ILogger logger, string? cookie, string userAgent, HttpClientHandler handler)
		{
			_logger = logger;
			if (string.IsNullOrEmpty(userAgent))
			{
				userAgent = Constants.IdmUserAgent;
			}
			var policy = new PooledHttpClientPolicy(() => HttpClientUtils.BuildClientForMultiThreadedDownloader(cookie, userAgent, handler));
			var provider = new DefaultObjectPoolProvider { MaximumRetained = 10 };
			_httpClientPool = provider.Create(policy);
		}

		/// <summary>
		/// 获取 Target 的文件大小
		/// </summary>
		/// <param name="token"></param>
		/// <returns></returns>
		private async ValueTask<long> GetContentLengthAsync(CancellationToken token)
		{
			token.ThrowIfCancellationRequested();

			using (_httpClientPool.GetObject(out var client))
			{
				var result = await client.GetAsync(Target, HttpCompletionOption.ResponseHeadersRead, token);

				var length = result.Content.Headers.ContentLength;
				if (length is not null)
				{
					return length.Value;
				}

				throw new HttpRequestException(@"Cannot get Content-Length");
			}
		}

		/// <summary>
		/// 开始下载，若获取大小失败，则会抛出异常
		/// </summary>
		public async ValueTask DownloadAsync(CancellationToken token)
		{
			StatusSubject.OnNext(@"正在获取下载文件大小...");
			FileSize = await GetContentLengthAsync(token); //总大小

			TempDir = EnsureDirectory(TempDir);
			var list = GetFileRangeList();

			var opQueue = new OperationQueue(1);
			Current = 0;
			Last = 0;
			try
			{
				using var speedMonitor = CreateSpeedMonitor();

				StatusSubject.OnNext(@"正在下载...");
				await list.Select(info =>
						// ReSharper disable once AccessToDisposedClosure
						opQueue.Enqueue(1, () => GetStreamAsync(info, token))
								.ToObservable()
								.SelectMany(res => WriteToFileAsync(res.Item1, res.Item2, token))
				).Merge();

				StatusSubject.OnNext(@"下载完成，正在合并文件...");
				Current = 0;
				await MergeFilesAsync(list, token);
			}
			catch (OperationCanceledException)
			{
				StatusSubject.OnNext(@"下载已取消");
				throw;
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, @"下载出错");
				StatusSubject.OnNext(@"下载出错");
			}
			finally
			{
				await opQueue.ShutdownQueue();
				opQueue.Dispose();

				Task.Run(async () =>
				{
					foreach (var range in list)
					{
						await DeleteFileWithRetryAsync(range.FileName);
					}
				}, CancellationToken.None).NoWarning();
			}
		}

		private static string EnsureDirectory(string? path)
		{
			try
			{
				if (path is null)
				{
					return Directory.GetCurrentDirectory();
				}
				if (!Directory.Exists(path))
				{
					Directory.CreateDirectory(path);
				}
				return path;
			}
			catch
			{
				return Directory.GetCurrentDirectory();
			}
		}

		private string GetTempFileName()
		{
			return Path.Combine(TempDir, Path.GetRandomFileName());
		}

		private List<FileRange> GetFileRangeList()
		{
			var list = new List<FileRange>();

			var parts = Threads; //线程数
			var partSize = FileSize / parts; //每块大小

			_logger.LogDebug($@"总大小：{FileSize} ({Target})");
			_logger.LogDebug($@"每块大小：{partSize} ({Target})");

			for (var i = 1; i < parts; ++i)
			{
				var range = new RangeHeaderValue((i - 1) * partSize, i * partSize - 1);
				list.Add(new FileRange(range, GetTempFileName()));
			}

			var last = new RangeHeaderValue((parts - 1) * partSize, FileSize);
			list.Add(new FileRange(last, GetTempFileName()));

			return list;
		}

		private async Task<(Stream, string)> GetStreamAsync(FileRange info, CancellationToken token)
		{
			token.ThrowIfCancellationRequested();
			using (_httpClientPool.GetObject(out var client))
			{
				var request = new HttpRequestMessage { RequestUri = Target };
				request.Headers.ConnectionClose = false;
				request.Headers.Range = info.Range;

				var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);

				var stream = await response.Content.ReadAsStreamAsync(token);

				return (stream, info.FileName);
			}
		}

		private async Task<Unit> WriteToFileAsync(Stream stream, string tempFileName, CancellationToken token)
		{
			token.ThrowIfCancellationRequested();

			await using var fs = File.OpenWrite(tempFileName);
			await CopyStreamAsyncWithProgress(stream, fs, true, token);
			return Unit.Default;
		}

		private async ValueTask MergeFilesAsync(IEnumerable<FileRange> files, CancellationToken token)
		{
			token.ThrowIfCancellationRequested();

			var dir = Path.GetDirectoryName(OutFileName);
			dir = EnsureDirectory(dir);
			var path = Path.Combine(dir, Path.GetFileName(OutFileName) ?? Path.GetRandomFileName());

			await using var outFileStream = File.Create(path);
			try
			{
				foreach (var file in files)
				{
					await using (var inputFileStream = File.OpenRead(file.FileName))
					{
						await CopyStreamAsyncWithProgress(inputFileStream, outFileStream, false, token);
					}
					await DeleteFileWithRetryAsync(file.FileName);
				}
			}
			catch (Exception)
			{
				await DeleteFileWithRetryAsync(path);
				throw;
			}
		}

		private async ValueTask DeleteFileWithRetryAsync(string? filename, byte retryTime = 3)
		{
			if (filename is null || !File.Exists(filename))
			{
				return;
			}
			var i = 0;
			while (true)
			{
				try
				{
					File.Delete(filename);
				}
				catch (Exception) when (i < retryTime)
				{
					++i;
					await Task.Delay(TimeSpan.FromSeconds(1));
					continue;
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, $@"删除 {filename} 出错");
				}
				break;
			}
		}

		private async ValueTask CopyStreamAsyncWithProgress(Stream from, Stream to, bool reportSpeed, CancellationToken token, int bufferSize = 81920)
		{
			using var memory = MemoryPool<byte>.Shared.Rent(bufferSize);
			while (true)
			{
				var length = await from.ReadAsync(memory.Memory, token);
				if (length != 0)
				{
					await to.WriteAsync(memory.Memory.Slice(0, length), token);
					ReportProgress(length, reportSpeed);
				}
				else
				{
					break;
				}
			}
		}

		private void ReportProgress(long length, bool reportSpeed)
		{
			if (reportSpeed)
			{
				Interlocked.Add(ref Last, length);
			}
			Interlocked.Add(ref Current, length);
		}
	}
}
