namespace BilibiliLive.Wbi;

internal sealed class WbiKeyCache(Func<CancellationToken, Task<string>> fetchMixinKey, TimeProvider timeProvider)
{
	// The keys rotate about once a day, so an hourly refresh keeps signatures valid without hitting nav per request
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
			Volatile.Write(ref _entry, new Entry(mixinKey, timeProvider.GetUtcNow()));
			return mixinKey;
		}
		finally
		{
			_gate.Release();
		}
	}

	private string? TryGetFresh()
	{
		return Volatile.Read(ref _entry) is { } entry && timeProvider.GetUtcNow() - entry.FetchedAt < RefreshInterval ? entry.MixinKey : null;
	}

	private sealed record Entry(string MixinKey, DateTimeOffset FetchedAt);
}
