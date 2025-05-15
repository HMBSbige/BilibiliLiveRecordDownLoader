using BilibiliLiveRecordDownLoader.Http.Interfaces;
using BilibiliLiveRecordDownLoader.Http.Models;
using BilibiliLiveRecordDownLoader.Shared.Abstractions;
using BilibiliLiveRecordDownLoader.Shared.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Threading;
using Punchclock;
using System.Buffers;
using System.Net.Http.Headers;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;

namespace BilibiliLiveRecordDownLoader.Http.Clients;

public class MultiThreadedDownloader(ILogger<MultiThreadedDownloader> logger, HttpClient client) : ProgressBase, IDownloader, IHttpClient
{
	private readonly ILogger _logger = logger;

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

	public HttpClient Client { get; set; } = client;

	/// <summary>
	/// 获取 Target 的文件大小
	/// </summary>
	/// <param name="token"></param>
	/// <returns></returns>
	private async ValueTask<long> GetContentLengthAsync(CancellationToken token)
	{
		token.ThrowIfCancellationRequested();

		HttpResponseMessage result = await Client.GetAsync(Target, HttpCompletionOption.ResponseHeadersRead, token);

		long? length = result.Content.Headers.ContentLength;

		if (length is not null)
		{
			return length.Value;
		}

		throw new HttpRequestException(@"Cannot get Content-Length");
	}

	/// <summary>
	/// 开始下载，若获取大小失败，则会抛出异常
	/// </summary>
	public async ValueTask DownloadAsync(CancellationToken cancellationToken)
	{
		StatusSubject.OnNext(@"正在获取下载文件大小...");
		FileSize = await GetContentLengthAsync(cancellationToken);//总大小

		TempDir = EnsureDirectory(TempDir);
		List<FileRange> list = GetFileRangeList();

		OperationQueue opQueue = new(1);
		Current = 0;
		Last = 0;

		try
		{
			using IDisposable speedMonitor = CreateSpeedMonitor();

			StatusSubject.OnNext(@"正在下载...");
			await list.Select(info =>
				// ReSharper disable once AccessToDisposedClosure
				opQueue.Enqueue(1, () => GetStreamAsync(info, cancellationToken))
					.ToObservable()
					.SelectMany(res => WriteToFileAsync(res.Item1, res.Item2, cancellationToken))
			).Merge();

			StatusSubject.OnNext(@"下载完成，正在合并文件...");
			Current = 0;
			await MergeFilesAsync(list, cancellationToken);
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
					foreach (FileRange range in list)
					{
						await DeleteFileWithRetryAsync(range.FileName);
					}
				},
				CancellationToken.None).Forget();
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
		List<FileRange> list = new();

		ushort parts = Threads;//线程数
		long partSize = FileSize / parts;//每块大小

		_logger.LogDebug($@"总大小：{FileSize} ({Target})");
		_logger.LogDebug($@"每块大小：{partSize} ({Target})");

		for (int i = 1; i < parts; ++i)
		{
			RangeHeaderValue range = new((i - 1) * partSize, i * partSize - 1);
			list.Add(new FileRange(range, GetTempFileName()));
		}

		RangeHeaderValue last = new((parts - 1) * partSize, FileSize);
		list.Add(new FileRange(last, GetTempFileName()));

		return list;
	}

	private async Task<(Stream, string)> GetStreamAsync(FileRange info, CancellationToken token)
	{
		token.ThrowIfCancellationRequested();

		HttpRequestMessage request = new() { RequestUri = Target };
		request.Headers.ConnectionClose = false;
		request.Headers.Range = info.Range;

		HttpResponseMessage response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);

		Stream stream = await response.Content.ReadAsStreamAsync(token);

		return (stream, info.FileName);
	}

	private async Task<Unit> WriteToFileAsync(Stream stream, string tempFileName, CancellationToken token)
	{
		token.ThrowIfCancellationRequested();

		await using FileStream fs = File.Create(tempFileName);
		await CopyStreamWithProgressAsync(stream, fs, true, token);
		return Unit.Default;
	}

	private async ValueTask MergeFilesAsync(IEnumerable<FileRange> files, CancellationToken token)
	{
		token.ThrowIfCancellationRequested();

		string? dir = Path.GetDirectoryName(OutFileName);
		dir = EnsureDirectory(dir);
		string path = Path.Combine(dir, Path.GetFileName(OutFileName) ?? Path.GetRandomFileName());

		await using FileStream outFileStream = File.Create(path);

		try
		{
			foreach (FileRange file in files)
			{
				await using (FileStream inputFileStream = File.OpenRead(file.FileName))
				{
					await CopyStreamWithProgressAsync(inputFileStream, outFileStream, false, token);
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

		int i = 0;

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

	private async ValueTask CopyStreamWithProgressAsync(Stream from, Stream to, bool reportSpeed, CancellationToken token, int bufferSize = 81920)
	{
		using IMemoryOwner<byte> memory = MemoryPool<byte>.Shared.Rent(bufferSize);

		while (true)
		{
			int length = await from.ReadAsync(memory.Memory, token);

			if (length != 0)
			{
				await to.WriteAsync(memory.Memory[..length], token);
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
