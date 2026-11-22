using BilibiliLive.Streams;

namespace BilibiliLive.Tests.Streams;

public class SegmentBufferTest
{
	[Test]
	public async Task GrowsPastItsFirstRentalAndKeepsEveryByteAsync(CancellationToken cancellationToken)
	{
		byte[] content = [.. Enumerable.Range(0, SegmentBuffer.MinimumCapacity * 5 + 7).Select(i => (byte)(i * 31))];
		using SegmentBuffer buffer = new(0);
		using MemoryStream source = new(content);

		await buffer.ReadFromAsync(source, cancellationToken);

		await Assert.That(buffer.Memory.ToArray().AsSpan().SequenceEqual(content)).IsTrue();
	}

	[Test]
	public async Task BodyOfTheAnnouncedLengthThatFillsTheRentalDoesNotGrowItAsync(CancellationToken cancellationToken)
	{
		byte[] content = [.. Enumerable.Range(0, SegmentBuffer.MinimumCapacity).Select(i => (byte)(i * 31))];
		using SegmentBuffer buffer = new(content.Length, content.Length);
		using MemoryStream source = new(content);

		await buffer.ReadFromAsync(source, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(buffer.Memory.ToArray().AsSpan().SequenceEqual(content)).IsTrue();
			await Assert.That(buffer.Capacity).IsEqualTo(content.Length);
		}
	}

	[Test]
	public async Task BodyLongerThanAnnouncedKeepsEveryByteAsync(CancellationToken cancellationToken)
	{
		byte[] content = [.. Enumerable.Range(0, SegmentBuffer.MinimumCapacity * 2 + 3).Select(i => (byte)(i * 31))];
		using SegmentBuffer buffer = new(SegmentBuffer.MinimumCapacity, SegmentBuffer.MinimumCapacity);
		using MemoryStream source = new(content);

		await buffer.ReadFromAsync(source, cancellationToken);

		await Assert.That(buffer.Memory.ToArray().AsSpan().SequenceEqual(content)).IsTrue();
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
