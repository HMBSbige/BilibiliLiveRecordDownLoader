using BilibiliLiveRecordDownLoader.FlvProcessor;
using BilibiliLiveRecordDownLoader.Http.DownLoaders;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.WindowsAPICodePack.Shell;
using System;
using System.IO;
using System.Threading.Tasks;

namespace UnitTest
{
    [TestClass]
    public class UnitTest
    {
        [TestMethod]
        public async Task TestDownloadAsync()
        {
            const string url = @"https://upload.wikimedia.org/wikipedia/commons/thumb/b/b6/Image_created_with_a_mobile_phone.png/1200px-Image_created_with_a_mobile_phone.png";
            const string filename = @"test.png";
            var path = KnownFolders.Downloads.Path;
            var outFile = Path.Combine(path, filename);

            var downloader = new MultiThreadedDownload();
            await downloader.DownloadFile(url, 4, outFile, path, Console.WriteLine);

            Assert.IsTrue(File.Exists(outFile));
            Assert.AreEqual(new FileInfo(outFile).Length, 1594447);

            File.Delete(outFile);
        }

        [TestMethod]
        public void TestFlvMerge()
        {
            const string f1 = @"C:\Users\Bruce\Downloads\1.flv";
            const string f2 = @"C:\Users\Bruce\Downloads\2.flv";
            const string outfile = @"C:\Users\Bruce\Downloads\test1.flv";

            var flv = new FlvMerger();
            flv.Add(f1);
            flv.Add(f2);
            flv.Merge(outfile);
        }
    }
}
