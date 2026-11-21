using BilibiliLive.Streams;
using BilibiliLive.Tests.Fakes;
using System.Buffers.Binary;

namespace BilibiliLive.Tests.Streams;

public class MediaProbeTest
{
	[Test]
	public async Task TransportStreamNeedsSeveralAlignedPacketsAsync()
	{
		byte[] shifted = [0x00, 0x00, .. MediaSamples.TransportStream("x")];

		using (Assert.Multiple())
		{
			await Assert.That(MediaProbe.IsTransportStream(MediaSamples.TransportStream("x"))).IsTrue();
			await Assert.That(MediaProbe.IsTransportStream(shifted)).IsTrue();
			await Assert.That(MediaProbe.IsTransportStream(MediaSamples.TransportStream("x", MediaProbe.TransportPacketsToCheck - 1))).IsFalse();
			await Assert.That(MediaProbe.IsTransportStream(new byte[MediaProbe.TransportPacketSize * MediaProbe.TransportPacketsToCheck])).IsFalse();
		}
	}

	[Test]
	public async Task IsoBmffRequiresTheBoxOfItsKindAsync()
	{
		using (Assert.Multiple())
		{
			await Assert.That(MediaProbe.IsIsoBmff(MediaSamples.Fmp4Init("x"), true)).IsTrue();
			await Assert.That(MediaProbe.IsIsoBmff(MediaSamples.Fmp4Fragment("x"), false)).IsTrue();
			await Assert.That(MediaProbe.IsIsoBmff(MediaSamples.Fmp4Fragment("x"), true)).IsFalse();
			await Assert.That(MediaProbe.IsIsoBmff(MediaSamples.Fmp4Init("x"), false)).IsFalse();
		}
	}

	[Test]
	public async Task IsoBmffRejectsBoxesThatDoNotTileTheDataAsync()
	{
		byte[] fragment = MediaSamples.Fmp4Fragment("x");
		byte[] oversized = [.. fragment];
		BinaryPrimitives.WriteUInt32BigEndian(oversized, (uint)fragment.Length + 1);

		using (Assert.Multiple())
		{
			await Assert.That(MediaProbe.IsIsoBmff(fragment.AsSpan(0, fragment.Length - 1), false)).IsFalse();
			await Assert.That(MediaProbe.IsIsoBmff(oversized, false)).IsFalse();
			await Assert.That(MediaProbe.IsIsoBmff("<html>blocked</html>"u8, false)).IsFalse();
			await Assert.That(MediaProbe.IsIsoBmff([], false)).IsFalse();
		}
	}

	[Test]
	public async Task IsoBmffFollowsLargeAndOpenEndedSizesAsync()
	{
		byte[] large = new byte[24];
		BinaryPrimitives.WriteUInt32BigEndian(large, 1);
		"moof"u8.CopyTo(large.AsSpan(4));
		BinaryPrimitives.WriteUInt64BigEndian(large.AsSpan(8), (ulong)large.Length);
		byte[] openEnded = [.. MediaSamples.Box("moof", []), 0, 0, 0, 0, .. "mdat"u8, 1, 2, 3];

		using (Assert.Multiple())
		{
			await Assert.That(MediaProbe.IsIsoBmff(large, false)).IsTrue();
			await Assert.That(MediaProbe.IsIsoBmff(openEnded, false)).IsTrue();
		}
	}
}
