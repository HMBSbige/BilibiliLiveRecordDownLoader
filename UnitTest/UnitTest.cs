using BilibiliLiveRecordDownLoader.FlvProcessor.Clients;
using BilibiliLiveRecordDownLoader.Http.Clients;
using BilibiliLiveRecordDownLoader.Shared.Utils;
using CryptoBase.Abstractions.Digests;
using CryptoBase.DataFormatExtensions;
using CryptoBase.Digests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Diagnostics;

namespace UnitTest;

[TestClass]
public class UnitTest
{
	private static async Task<string> CalculateSHA256Async(string filename)
	{
		using IHash hasher = DigestUtils.Create(DigestType.Sha256);
		await using FileStream stream = File.OpenRead(filename);
		byte[] hash = await hasher.ComputeHashAsync(stream);
		return hash.AsSpan().ToHexString();
	}

	private static async Task<string> DownloadAsync()
	{
		const string url = @"https://www.mediacollege.com/video-gallery/testclips/4sec.flv";
		var filename = Path.ChangeExtension(Path.GetRandomFileName(), @"flv");
		const string sha256 = @"9657166E7865880954FD6BEE8A7F9E2BBF2F32D7729BB8184A2AA2BA1261FAB6";
		var path = Path.GetTempPath();
		var outFile = Path.Combine(path, filename);
		try
		{
			var client = HttpClientUtils.BuildClientForMultiThreadedDownloader(default, string.Empty, new SocketsHttpHandler());
			await using var downloader = new MultiThreadedDownloader(NullLogger<MultiThreadedDownloader>.Instance, client)
			{
				Target = new(url),
				Threads = 4,
				OutFileName = outFile,
				TempDir = path
			};

			//downloader.ProgressUpdated.Subscribe(i => { Console.WriteLine($@"{i * 100:F2}%"); });
			//downloader.CurrentSpeed.Subscribe(i => { Console.WriteLine($@"{i} Bytes/s"); });

			await downloader.DownloadAsync(default);

			Assert.IsTrue(File.Exists(outFile));
			Assert.AreEqual(await CalculateSHA256Async(outFile), sha256);

			return outFile;
		}
		catch (Exception)
		{
			File.Delete(outFile);
			throw;
		}
	}

	[TestMethod]
	public async Task TestFlvMergeAsync()
	{
		const string sha256 = @"1FA16CA4A31343F43262E90E31EF63C8247574B14F4764D1BFB37AFDEDF3EB84";

		var outFile = await DownloadAsync();
		var path = Path.GetTempPath();
		var outFlv = Path.Combine(path, @"test.flv");
		try
		{
			var flvMerger = new FlvMerger(NullLogger<FlvMerger>.Instance);
			flvMerger.Add(outFile);
			flvMerger.Add(outFile);

			var sw = Stopwatch.StartNew();
			await flvMerger.MergeAsync(outFlv, default);
			sw.Stop();
			Console.WriteLine(sw.Elapsed.TotalSeconds);

			Assert.IsTrue(File.Exists(outFlv));
			Assert.AreEqual(await CalculateSHA256Async(outFlv), sha256);
		}
		finally
		{
			File.Delete(outFile);
			File.Delete(outFlv);
		}
	}

	[TestMethod]
	public async Task DownloadTestAsync()
	{
		const string url = @"https://www.mediacollege.com/video-gallery/testclips/4sec.flv";
		var filename = Path.ChangeExtension(Path.GetRandomFileName(), @"flv");
		const string sha256 = @"9657166E7865880954FD6BEE8A7F9E2BBF2F32D7729BB8184A2AA2BA1261FAB6";
		var path = Path.GetTempPath();
		var outFile = Path.Combine(path, filename);
		try
		{
			var client = HttpClientUtils.BuildClientForBilibili(string.Empty, default, new SocketsHttpHandler());
			await using var downloader = new HttpDownloader(client)
			{
				Target = new(url),
				OutFileName = outFile
			};

			//downloader.CurrentSpeed.Subscribe(i => { Console.WriteLine($@"{i} Bytes/s"); });

			await downloader.DownloadAsync(default);

			Assert.IsTrue(File.Exists(outFile));
			Assert.AreEqual(await CalculateSHA256Async(outFile), sha256);
		}
		catch (Exception)
		{
			File.Delete(outFile);
			throw;
		}
		finally
		{
			File.Delete(outFile);
		}
	}

	[TestMethod]
	public async Task TestNtpAsync()
	{
		var time = await Ntp.GetCurrentTimeAsync();
		var now = DateTime.UtcNow;
		Assert.IsTrue((now - time).Duration() < TimeSpan.FromSeconds(1));
	}

	[TestMethod]
	public void TestTimestamp()
	{
		var time = DateTime.UtcNow;
		var timestamp = Timestamp.GetTimestamp(time);
		var t2 = Timestamp.GetTime(timestamp);
		Assert.IsTrue(time >= t2);
		Assert.IsTrue(time - t2 < TimeSpan.FromSeconds(1));
	}
}
