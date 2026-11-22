namespace BilibiliLive.Wbi;

internal sealed class WbiKeyCache(Func<CancellationToken, Task<string>> fetchMixinKey, TimeProvider timeProvider)
{
	// The keys rotate about once a day
	internal static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(1);

	private readonly SemaphoreSlim _gate = new(1, 1);

	private Entry? _entry;

	public async ValueTask<string> GetMixinKeyAsync(CancellationToken cancellationToken)
	{
		if (TryGetFresh() is { } cached)
		{
			return cached;
		}

		await _gate.WaitAsync(cancellationToken);

		try
		{
			if (TryGetFresh() is { } refreshed)
			{
				return refreshed;
			}

			string mixinKey = await fetchMixinKey(cancellationToken);
			Store(mixinKey);
			return mixinKey;
		}
		finally
		{
			_gate.Release();
		}
	}

	public void Store(string mixinKey)
	{
		Volatile.Write(ref _entry, new Entry(mixinKey, timeProvider.GetUtcNow()));
	}

	// Wall time rather than a monotonic clock, which stops while Linux and macOS sleep although the keys keep rotating by the calendar
	private string? TryGetFresh()
	{
		if (Volatile.Read(ref _entry) is not { } entry)
		{
			return null;
		}

		// A clock set back past the fetch would otherwise keep the key fresh for as long as it was set back
		TimeSpan age = timeProvider.GetUtcNow() - entry.FetchedAt;
		return age >= TimeSpan.Zero && age < RefreshInterval ? entry.MixinKey : null;
	}

	private sealed record Entry(string MixinKey, DateTimeOffset FetchedAt);
}
