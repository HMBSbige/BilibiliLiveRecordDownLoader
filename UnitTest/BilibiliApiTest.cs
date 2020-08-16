using System.Threading.Tasks;
using BilibiliLiveRecordDownLoader.BilibiliApi;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTest
{
    [TestClass]
    public class BilibiliApiTest
    {
        [TestMethod]
        public async Task LiveRecordUrlTest()
        {
            using var client = new BililiveApiClient();
            var json = await client.GetLiveRecordUrl(@"R1nx411c7b1");
            Assert.AreEqual(json.code, 0);
            Assert.AreEqual(json.message, @"0");
            Assert.AreEqual(json.data.size, 13584350482);
            Assert.AreEqual(json.data.length, 18097042);
            Assert.AreEqual(json.data.list.Length, 11);
        }
    }
}
