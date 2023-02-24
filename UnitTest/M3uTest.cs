using BilibiliApi.Model;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text;

namespace UnitTest;

[TestClass]
public class M3uTest
{
	[TestMethod]
	public void ParseTest0()
	{
		const string content = """
		#EXTM3U
		#EXT-X-VERSION:3
		#EXT-X-TARGETDURATION:13
		#EXT-X-PLAYLIST-TYPE:EVENT
		#EXT-X-MEDIA-SEQUENCE:0
		#EXTINF:10.000,
		0.ts
		#EXTINF:10.000,
		1.ts
		#EXT-X-ENDLIST
		#EXTINF:10.000,
		2.ts
		""";
		using MemoryStream stream = new(Encoding.UTF8.GetBytes(content));
		M3U m3u8 = new(stream);
		Assert.AreEqual(3, m3u8.Version);
		Assert.AreEqual(2, m3u8.Segments.Count);
		Assert.AreEqual(@"0.ts", m3u8.Segments[0]);
		Assert.AreEqual(@"1.ts", m3u8.Segments[1]);
	}

	[TestMethod]
	public void ParseTest1()
	{
		const string content = """
		#EXTM3U
		#EXT-X-VERSION:3
		#EXT-X-MEDIA-SEQUENCE:137
		#EXT-X-TARGETDURATION:3
		#EXTINF:2.996, no desc
		/live-bvc/live_2051617240_30614556/1677132022560.ts?trid=100366f733ecb33e11edbec4850f533e18ab
		#EXTINF:2.982, no desc
		/live-bvc/live_2051617240_30614556/1677132025521.ts?trid=100366f733ecb33e11edbec4850f533e18ab
		#EXTINF:2.990, no desc
		/live-bvc/live_2051617240_30614556/1677132028588.ts?trid=100366f733ecb33e11edbec4850f533e18ab
		""";
		using MemoryStream stream = new(Encoding.UTF8.GetBytes(content));
		M3U m3u8 = new(stream);
		Assert.AreEqual(3, m3u8.Version);
		Assert.AreEqual(3, m3u8.Segments.Count);
		Assert.AreEqual(@"/live-bvc/live_2051617240_30614556/1677132022560.ts?trid=100366f733ecb33e11edbec4850f533e18ab", m3u8.Segments[0]);
		Assert.AreEqual(@"/live-bvc/live_2051617240_30614556/1677132025521.ts?trid=100366f733ecb33e11edbec4850f533e18ab", m3u8.Segments[1]);
		Assert.AreEqual(@"/live-bvc/live_2051617240_30614556/1677132028588.ts?trid=100366f733ecb33e11edbec4850f533e18ab", m3u8.Segments[2]);
	}

	[TestMethod]
	public void ParseTest2()
	{
		const string content = """
		#EXTM3U
		#EXT-X-VERSION:3
		#EXT-X-MEDIA-SEQUENCE:13
		#EXT-X-TARGETDURATION:3
		#EXT-X-DISCONTINUITY
		#EXTINF:2.996, no desc
		/live-bvc/live_50329118_9516950/1677145949976.ts?trid=1003c95a6a56b35f11ed81956d7a65f9b559
		""";
		using MemoryStream stream = new(Encoding.UTF8.GetBytes(content));
		M3U m3u8 = new(stream);
		Assert.AreEqual(3, m3u8.Version);
		Assert.AreEqual(1, m3u8.Segments.Count);
		Assert.AreEqual(@"/live-bvc/live_50329118_9516950/1677145949976.ts?trid=1003c95a6a56b35f11ed81956d7a65f9b559", m3u8.Segments[0]);
	}

	[TestMethod]
	public void ParseTest3()
	{
		const string content = """
		#EXTM3U
		#EXT-X-VERSION:7
		#EXT-X-START:TIME-OFFSET=0
		#EXT-X-MEDIA-SEQUENCE:36179519
		#EXT-X-TARGETDURATION:1
		#EXT-X-MAP:URI="h1676709149.m4s"
		#EXTINF:1.00,12f6bf|dc965288
		36179519.m4s
		#EXTINF:1.00,14011f|bdfa71e8
		36179520.m4s
		#EXTINF:1.00,14efc2|b7f77daa
		36179521.m4s
		#EXTINF:1.00,143211|495264dd
		36179522.m4s
		#EXTINF:1.00,13f738|63a7c23b
		36179523.m4s
		#EXTINF:1.00,152074|a06e6c58
		36179524.m4s
		#EXTINF:1.00,14e715|e4826aca
		36179525.m4s
		#EXTINF:1.00,136f66|bee0f50
		36179526.m4s
		""";
		using MemoryStream stream = new(Encoding.UTF8.GetBytes(content));
		M3U m3u8 = new(stream);
		Assert.AreEqual(7, m3u8.Version);
		Assert.AreEqual(8, m3u8.Segments.Count);
		Assert.AreEqual(@"36179519.m4s", m3u8.Segments[0]);
		Assert.AreEqual(@"36179520.m4s", m3u8.Segments[1]);
		Assert.AreEqual(@"36179521.m4s", m3u8.Segments[2]);
		Assert.AreEqual(@"36179522.m4s", m3u8.Segments[3]);
		Assert.AreEqual(@"36179523.m4s", m3u8.Segments[4]);
		Assert.AreEqual(@"36179524.m4s", m3u8.Segments[5]);
		Assert.AreEqual(@"36179525.m4s", m3u8.Segments[6]);
		Assert.AreEqual(@"36179526.m4s", m3u8.Segments[7]);
	}
}
