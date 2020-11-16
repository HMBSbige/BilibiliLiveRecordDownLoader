using BilibiliLiveRecordDownLoader.FlvProcessor.Clients;
using BilibiliLiveRecordDownLoader.Http.DownLoaders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.WindowsAPICodePack.Shell;
using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace UnitTest
{
	[TestClass]
	public class UnitTest
	{
		private static string CalculateSHA256(string filename)
		{
			using var sha256 = SHA256.Create();
			using var stream = File.OpenRead(filename);
			var hash = sha256.ComputeHash(stream);
			return BitConverter.ToString(hash)
					.Replace(@"-", string.Empty)
					.ToUpperInvariant();
		}

		private static async Task<string> DownloadAsync()
		{
			const string url = @"https://www.mediacollege.com/video-gallery/testclips/4sec.flv";
			var filename = Path.ChangeExtension(Path.GetRandomFileName(), @"flv");
			const string sha256 = @"9657166E7865880954FD6BEE8A7F9E2BBF2F32D7729BB8184A2AA2BA1261FAB6";
			var path = KnownFolders.Downloads.Path;
			var outFile = Path.Combine(path, filename);
			try
			{
				await using var downloader = new MultiThreadedDownloader(NullLogger<MultiThreadedDownloader>.Instance)
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
				Assert.AreEqual(CalculateSHA256(outFile), sha256);

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
			var outFlv = Path.Combine(KnownFolders.Downloads.Path, @"test.flv");
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
				Assert.AreEqual(CalculateSHA256(outFlv), sha256);
			}
			finally
			{
				File.Delete(outFile);
				File.Delete(outFlv);
			}
		}
	}
}
