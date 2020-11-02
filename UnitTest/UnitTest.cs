using BilibiliLiveRecordDownLoader.FlvProcessor;
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

        [TestMethod]
        public async Task TestDownloadAsync()
        {
            const string url = @"https://upload.wikimedia.org/wikipedia/commons/thumb/b/b6/Image_created_with_a_mobile_phone.png/1200px-Image_created_with_a_mobile_phone.png";
            var filename = Path.ChangeExtension(Path.GetTempFileName(), @"png");
            const string sha256 = @"179B41F1B27111836AC73F13B4C42F1034863AA0F4B00C8282D60C13711D7B9E";
            var path = KnownFolders.Downloads.Path;
            var outFile = Path.Combine(path, filename);

            await using var downloader = new MultiThreadedDownloader(NullLogger<MultiThreadedDownloader>.Instance)
            {
                Target = new Uri(url),
                Threads = 4,
                OutFileName = outFile,
                TempDir = path
            };

            //downloader.ProgressUpdated.Subscribe(i => { Console.WriteLine($@"{i * 100:F2}%"); });
            //downloader.CurrentSpeed.Subscribe(i => { Console.WriteLine($@"{i} Bytes/s"); });

            await downloader.DownloadAsync(default);

            Assert.IsTrue(File.Exists(outFile));
            Assert.AreEqual(new FileInfo(outFile).Length, 1594447);
            Assert.AreEqual(CalculateSHA256(outFile), sha256);

            File.Delete(outFile);
        }

        [TestMethod]
        public void TestFlvMerge()
        {
            var sw = new Stopwatch();
            sw.Start();

            const string f1 = @"C:\Users\Bruce\Downloads\1.flv";
            const string f2 = @"C:\Users\Bruce\Downloads\2.flv";
            const string outfile = @"F:\test1.flv";

            var flv = new FlvMerger();
            flv.Add(f1);
            flv.Add(f2);
            flv.Merge(outfile);

            sw.Stop();

            Console.WriteLine(sw.Elapsed.TotalSeconds);
        }

        [TestMethod]
        public async Task TestFlvMergeAsync()
        {
            var sw = new Stopwatch();
            sw.Start();

            const string f1 = @"C:\Users\Bruce\Downloads\1.flv";
            const string f2 = @"C:\Users\Bruce\Downloads\2.flv";
            const string outfile = @"F:\test2.flv";

            var flv = new FlvMerger();
            flv.Add(f1);
            flv.Add(f2);
            await flv.MergeAsync(outfile);

            sw.Stop();

            Console.WriteLine(sw.Elapsed.TotalSeconds);
        }
    }
}
