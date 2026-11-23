using BilibiliLive.Streams;

namespace BilibiliLive.Tests.Streams;

public class SegmentHistoryTest
{
	[Test]
	public async Task ForgetsOldestBeyondCapacityAsync()
	{
		SegmentHistory history = new(2);

		history.Add("a");
		history.Add("b");
		history.Add("a");

		await Assert.That(history)
			.Member(static h => h.Contains("a"), static a => a.IsTrue())
			.And.Member(static h => h.Contains("b"), static b => b.IsTrue());

		history.Add("c");
		history.Add("b");
		history.Add("a");

		await Assert.That(history)
			.Member(static h => h.Contains("a"), static a => a.IsTrue())
			.And.Member(static h => h.Contains("b"), static b => b.IsFalse())
			.And.Member(static h => h.Contains("c"), static c => c.IsTrue());
	}
}
