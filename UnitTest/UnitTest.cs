using System;
using System.IO;
using System.Threading.Tasks;
using BilibiliLiveRecordDownLoader.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.WindowsAPICodePack.Shell;

namespace UnitTest
{
    [TestClass]
    public class UnitTest
    {
        [TestMethod]
        public async Task TestDownload()
        {
            const string url = @"https://upload.wikimedia.org/wikipedia/commons/thumb/b/b6/Image_created_with_a_mobile_phone.png/1200px-Image_created_with_a_mobile_phone.png";
            const string filename = @"test.png";
            var path = KnownFolders.Downloads.Path;
            var outFile = Path.Combine(path, filename);

            var downloader = new Downloader();
            await downloader.DownloadFile(url, 4, outFile, path, Console.WriteLine);

            Assert.IsTrue(File.Exists(outFile));
            Assert.AreEqual(new FileInfo(outFile).Length, 1594447);

            File.Delete(outFile);
        }
    }
}
