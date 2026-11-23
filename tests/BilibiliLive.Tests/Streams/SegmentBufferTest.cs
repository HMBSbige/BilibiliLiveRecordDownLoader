using BilibiliLive.Streams;

namespace BilibiliLive.Tests.Streams;

public class SegmentBufferTest
{
	private static byte[] Content(int length)
	{
		return [.. Enumerable.Range(0, length).Select(static i => (byte)(i * 31))];
	}

	[Test]
	[Arguments(SegmentBuffer.MinimumCapacity * 5 + 7, 0, null)]
	[Arguments(SegmentBuffer.MinimumCapacity * 2 + 3, SegmentBuffer.MinimumCapacity, SegmentBuffer.MinimumCapacity)]
	public async Task GrowsPastItsFirstRentalAndKeepsEveryByteAsync(int length, int capacity, long? expectedLength, CancellationToken cancellationToken)
	{
		byte[] content = Content(length);
		using SegmentBuffer buffer = new(capacity, expectedLength);
		using MemoryStream source = new(content);

		await buffer.ReadFromAsync(source, cancellationToken);
		byte[] read = buffer.Memory.ToArray();

		await Assert.That((read.Length, read.AsSpan().CommonPrefixLength(content))).IsEqualTo((content.Length, content.Length));
	}

	[Test]
	public async Task BodyOfTheAnnouncedLengthThatFillsTheRentalDoesNotGrowItAsync(CancellationToken cancellationToken)
	{
		byte[] content = Content(SegmentBuffer.MinimumCapacity);
		using SegmentBuffer buffer = new(content.Length, content.Length);
		using MemoryStream source = new(content);

		await buffer.ReadFromAsync(source, cancellationToken);
		byte[] read = buffer.Memory.ToArray();

		using (Assert.Multiple())
		{
			await Assert.That((read.Length, read.AsSpan().CommonPrefixLength(content))).IsEqualTo((content.Length, content.Length));
			await Assert.That(buffer.Capacity).IsEqualTo(content.Length);
		}
	}

	[Test]
	public async Task DisposingTwiceLeavesNothingToReadAsync(CancellationToken cancellationToken)
	{
		SegmentBuffer buffer = new(16);
		using MemoryStream source = new([1, 2, 3]);
		await buffer.ReadFromAsync(source, cancellationToken);

		buffer.Dispose();
		buffer.Dispose();

		using (Assert.Multiple())
		{
			await Assert.That(buffer.Memory.Length).IsEqualTo(0);
			await Assert.That(() => buffer.ReadFromAsync(source, cancellationToken)).Throws<ObjectDisposedException>();
		}
	}
}
