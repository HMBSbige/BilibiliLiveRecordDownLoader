using TUnit.Assertions.Enums;

namespace BilibiliLive.Tests;

// The comparer overload of IsEquivalentTo avoids the reflection-based structural comparison that the trim analyzer rejects
internal static class Sequences
{
	public static async Task AssertEqualAsync<T>(IEnumerable<T> actual, IEnumerable<T> expected)
	{
		await Assert.That(actual.ToArray()).IsEquivalentTo(expected, EqualityComparer<T>.Default, CollectionOrdering.Matching);
	}

	public static async Task AssertEquivalentAsync<T>(IEnumerable<T> actual, IEnumerable<T> expected)
	{
		await Assert.That(actual.ToArray()).IsEquivalentTo(expected, EqualityComparer<T>.Default, CollectionOrdering.Any);
	}
}
