using BilibiliLive.Streams;

namespace BilibiliLive.Tests.Streams;

public class SegmentHistoryTest
{
	[Test]
	public async Task ForgetsOldestBeyondCapacityAsync()
	{
		SegmentHistory history = new(2);

		bool[] added = [history.Add("a"), history.Add("b"), history.Add("a"), history.Add("c"), history.Add("b"), history.Add("a")];

		using (Assert.Multiple())
		{
			await Sequences.AssertEqualAsync(added, [true, true, false, true, false, true]);
			await Assert.That(history.Count).IsEqualTo(2);
		}
	}
}
