using System.Diagnostics.CodeAnalysis;

namespace BilibiliLive.Wbi;

[SuppressMessage("Design", "CA1001", Justification = "只用 WaitAsync 和 Release，不会创建需要释放的等待句柄")]
internal sealed class WbiKeyCache(Func<CancellationToken, Task<string>> fetchMixinKey, TimeProvider timeProvider)
{
	// 密钥大约每天轮换一次
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

	// 用墙上时间而不是单调时钟：Linux 和 macOS 休眠时单调时钟会停，密钥却照样按日历轮换
	private string? TryGetFresh()
	{
		if (Volatile.Read(ref _entry) is not { } entry)
		{
			return null;
		}

		// 时钟被调回到获取之前时 age 为负，不排除的话密钥会多用调回的那么久
		TimeSpan age = timeProvider.GetUtcNow() - entry.FetchedAt;
		return age >= TimeSpan.Zero && age < RefreshInterval ? entry.MixinKey : null;
	}

	private sealed record Entry(string MixinKey, DateTimeOffset FetchedAt);
}
