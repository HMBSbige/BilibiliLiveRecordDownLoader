using BilibiliLive.Streams;

namespace BilibiliLive.Tests.Streams;

public class HlsPlaylistTest
{
	[Test]
	public async Task ParsesBilibiliFmp4PlaylistAsync()
	{
		const string content = """
			#EXTM3U
			#EXT-X-VERSION:7
			#EXT-X-START:TIME-OFFSET=0
			#EXT-X-MEDIA-SEQUENCE:328033198
			#EXT-X-TARGETDURATION:1
			#EXT-X-MAP:URI="h1795240925.m4s"
			#EXT-BILI-AUX:1049ec0|K|2b2b7|70e21dc6
			#EXTINF:1.00,2b2b7|70e21dc6
			328033198.m4s
			#EXT-BILI-AUX:104a244|N|2603a|75ed7701
			#EXTINF:1.00,2603a|75ed7701
			328033199.m4s
			""";

		HlsPlaylist playlist = HlsPlaylist.Parse(content);

		using (Assert.Multiple())
		{
			await Assert.That(playlist.TargetDuration).IsEqualTo(TimeSpan.FromSeconds(1));
			await Sequences.AssertEqualAsync(playlist.Segments, [new HlsSegment("328033198.m4s", "h1795240925.m4s", 0, 328033198), new HlsSegment("328033199.m4s", "h1795240925.m4s", 0, 328033199)]);
			await Assert.That(playlist.EndList).IsFalse();
		}
	}

	[Test]
	public async Task ParsesTsPlaylistWithEndListAndCrLfAsync()
	{
		string content = "\uFEFF#EXTM3U\r\n#EXT-X-VERSION:3\r\n#EXT-X-TARGETDURATION:3\r\n#EXT-X-PROGRAM-DATE-TIME:2026-11-21T18:46:26+08:00\r\n#EXTINF:3.000, no desc\r\n  live-1.ts  \r\n\r\n#EXTINF:3.000, no desc\r\nhttps://cdn.test/live-2.ts?x=1\r\n#EXT-X-ENDLIST\r\n";

		HlsPlaylist playlist = HlsPlaylist.Parse(content);

		using (Assert.Multiple())
		{
			await Assert.That(playlist.TargetDuration).IsEqualTo(TimeSpan.FromSeconds(3));
			await Sequences.AssertEqualAsync(playlist.Segments, [new HlsSegment("live-1.ts", null, 0, null), new HlsSegment("https://cdn.test/live-2.ts?x=1", null, 0, null)]);
			await Assert.That(playlist.EndList).IsTrue();
		}
	}

	[Test]
	public async Task DefaultsWhenTagsAreMissingAsync()
	{
		HlsPlaylist playlist = HlsPlaylist.Parse("#EXTM3U\n");

		using (Assert.Multiple())
		{
			await Assert.That(playlist.TargetDuration).IsNull();
			await Assert.That(playlist.Segments).IsEmpty();
		}
	}

	[Test]
	public async Task EachSegmentKeepsTheMapThatPrecedesItAsync()
	{
		HlsPlaylist playlist = HlsPlaylist.Parse("#EXTM3U\n#EXT-X-MAP:URI=\"h1.m4s\"\n1.m4s\n#EXT-X-MAP:URI=\"h2.m4s\"\n2.m4s\n");

		await Sequences.AssertEqualAsync(playlist.Segments, [new HlsSegment("1.m4s", "h1.m4s", 0, null), new HlsSegment("2.m4s", "h2.m4s", 0, null)]);
	}

	[Test]
	public async Task DiscontinuitiesCountFromTheSequenceTagAsync()
	{
		HlsPlaylist playlist = HlsPlaylist.Parse("#EXTM3U\n#EXT-X-DISCONTINUITY-SEQUENCE:7\n1.ts\n#EXT-X-DISCONTINUITY\n2.ts\n3.ts\n#EXT-X-DISCONTINUITY\n4.ts\n");

		await Sequences.AssertEqualAsync(playlist.Segments.Select(s => s.DiscontinuitySequence), [7L, 8L, 8L, 9L]);
	}

	[Test]
	public async Task SegmentsAreNumberedFromTheMediaSequenceTagAsync()
	{
		HlsPlaylist playlist = HlsPlaylist.Parse("#EXTM3U\n#EXT-X-MEDIA-SEQUENCE:41\n1.ts\n#EXT-X-DISCONTINUITY\n2.ts\n3.ts\n");

		await Sequences.AssertEqualAsync(playlist.Segments.Select(s => s.Sequence), [41L, 42L, 43L]);
	}

	[Test]
	public async Task UnusedVersionTagIsIgnoredAsync()
	{
		HlsPlaylist playlist = HlsPlaylist.Parse("#EXTM3U\n#EXT-X-VERSION:x\n1.ts\n");

		await Assert.That(playlist.Segments.Count).IsEqualTo(1);
	}

	[Test]
	[Arguments("""#EXT-X-MAP:URI="init.mp4",BYTERANGE="720@0" """, "init.mp4")]
	[Arguments("""#EXT-X-MAP:BYTERANGE="720@0",URI="init,with,commas.mp4" """, "init,with,commas.mp4")]
	public async Task MapUriIsReadFromAttributeListAsync(string mapLine, string expected)
	{
		HlsPlaylist playlist = HlsPlaylist.Parse($"#EXTM3U\n{mapLine}\nseg.m4s\n");

		await Assert.That(playlist.Segments[0].MapUri).IsEqualTo(expected);
	}

	[Test]
	public async Task FindsTheVariantOfABilibiliMasterPlaylistAsync()
	{
		const string content = """
			#EXTM3U
			#EXT-X-VERSION:7
			#EXT-X-STREAM-INF:PROGRAM-ID=1,BANDWIDTH=2560000
			https://edge.test/d1--cn-gotcha204b.bilivideo.com/live-bvc/1/live_2500/index.m3u8?expires=1&sign=x
			""";

		using (Assert.Multiple())
		{
			await Assert.That(HlsPlaylist.GetVariant(content)).IsEqualTo("https://edge.test/d1--cn-gotcha204b.bilivideo.com/live-bvc/1/live_2500/index.m3u8?expires=1&sign=x");
			await Assert.That(() => HlsPlaylist.Parse(content)).Throws<FormatException>();
		}
	}

	[Test]
	public async Task MediaPlaylistHasNoVariantAsync()
	{
		await Assert.That(HlsPlaylist.GetVariant("#EXTM3U\n#EXT-X-MAP:URI=\"h1.m4s\"\n#EXTINF:1.00,\n1.m4s\n")).IsNull();
	}

	[Test]
	[Arguments("")]
	[Arguments("<html></html>")]
	[Arguments("#EXTM3U\n#EXT-X-TARGETDURATION:abc\n")]
	[Arguments("#EXTM3U\n#EXT-X-MEDIA-SEQUENCE:-1\n")]
	[Arguments("#EXTM3U\n#EXT-X-MAP:BYTERANGE=\"1@0\"\n")]
	public async Task RejectsInvalidContentAsync(string content)
	{
		await Assert.That(() => HlsPlaylist.Parse(content)).Throws<FormatException>();
	}
}
