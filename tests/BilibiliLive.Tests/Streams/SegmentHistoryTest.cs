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
			await Assert.That(added).IsEquivalentTo([true, true, false, true, false, true], CollectionOrdering.Matching);
			await Assert.That(history.Count).IsEqualTo(2);
		}
	}
}
