using System.Text;

namespace BilibiliLiveRecordDownLoader.Application.Danmaku;

// Peak 是热度最高的那一格的开始；Phrase 在事件里只有抽奖口令或复读时为空
internal sealed record DanmakuHighlight(TimeSpan Start, TimeSpan Peak, TimeSpan End, double Score, string Phrase);

internal static class DanmakuHighlightDetector
{
	private const double BaselineRatio = 2.5;

	// 冷清的直播间基线接近 0，没有下限的话几个人同时说话就算高能
	private const double MinimumScore = 10;

	// 一格里某句话的人数超过这个比例时，多半是抽奖口令或复读，只按 DominantPhraseLimit 个人算
	private const double DominantPhraseShare = 0.5;

	private const int DominantPhraseLimit = 3;

	private const double MaxPerHour = 6;

	private const int MaxPhraseLength = 15;

	private static readonly TimeSpan BinWidth = TimeSpan.FromSeconds(5);

	// 基线取前后各这么长的中位数，直播间整体变热时不会把整段都当成高能
	private static readonly TimeSpan BaselineWindow = TimeSpan.FromMinutes(10);

	private static readonly TimeSpan MinimumSeparation = TimeSpan.FromMinutes(3);

	// duration 是视频时长，为 null 时按最后一条弹幕算
	public static IReadOnlyList<DanmakuHighlight> Detect(IEnumerable<DanmakuRecord> records, TimeSpan? duration = null)
	{
		DanmakuRecord.Comment[] comments = records.OfType<DanmakuRecord.Comment>().Where(static comment => !comment.IsStorm && comment.Time >= TimeSpan.Zero).OrderBy(static comment => comment.Time).ToArray();

		if (comments.Length is 0)
		{
			return [];
		}

		TimeSpan total = duration is { } known && known > comments[^1].Time ? known : comments[^1].Time + BinWidth;
		int binCount = (int)((comments[^1].Time.Ticks / BinWidth.Ticks) + 1);
		binCount = Math.Max(binCount, (int)Math.Ceiling(total.Ticks / (double)BinWidth.Ticks));
		List<Message>?[] bins = new List<Message>?[binCount];

		for (int i = 0; i < comments.Length; ++i)
		{
			DanmakuRecord.Comment comment = comments[i];
			int bin = (int)(comment.Time.Ticks / BinWidth.Ticks);
			(bins[bin] ??= []).Add(new Message(GetUserKey(comment, i), Normalize(comment.Text), comment.Text, comment.Time));
		}

		double[] scores = new double[binCount];
		string?[] dominantPhrases = new string?[binCount];

		for (int i = 0; i < binCount; ++i)
		{
			(scores[i], dominantPhrases[i]) = bins[i] is { } messages ? Score(messages) : (0, null);
		}

		double[] baselines = ComputeBaselines(scores, (int)(BaselineWindow.Ticks / BinWidth.Ticks));
		List<DanmakuHighlight> candidates = [];

		for (int i = 0; i < binCount;)
		{
			if (!IsHot(i))
			{
				++i;
				continue;
			}

			int first = i;
			int peak = i;

			for (; i < binCount && IsHot(i); ++i)
			{
				if (scores[i] > scores[peak])
				{
					peak = i;
				}
			}

			TimeSpan start = BinWidth * first;
			TimeSpan end = BinWidth * i;
			HashSet<string> excluded = new(dominantPhrases.Skip(first).Take(i - first).OfType<string>(), StringComparer.Ordinal);
			candidates.Add(new DanmakuHighlight(start, BinWidth * peak, end < total ? end : total, scores[peak], FindPhrase(bins.AsSpan(first, i - first), excluded, MaxPhraseLength)));
		}

		List<DanmakuHighlight> selected = [];
		int limit = Math.Max(1, (int)Math.Ceiling(total.TotalHours * MaxPerHour));

		foreach (DanmakuHighlight candidate in candidates.OrderByDescending(static highlight => highlight.Score).ThenBy(static highlight => highlight.Start))
		{
			if (selected.Count >= limit)
			{
				break;
			}

			if (selected.All(other => (other.Peak - candidate.Peak).Duration() >= MinimumSeparation))
			{
				selected.Add(candidate);
			}
		}

		return selected.OrderBy(static highlight => highlight.Start).ToArray();

		bool IsHot(int bin)
		{
			return scores[bin] >= MinimumScore && scores[bin] >= baselines[bin] * BaselineRatio;
		}
	}

	// 按不同用户计数，一个人刷屏只算一次
	private static (double Score, string? Dominant) Score(List<Message> messages)
	{
		HashSet<string> users = [.. messages.Select(static message => message.User)];
		IGrouping<string, Message>? dominant = messages.GroupBy(static message => message.Phrase, StringComparer.Ordinal)
			.Select(static group => (Group: group, Users: group.Select(static message => message.User).Distinct(StringComparer.Ordinal).Count()))
			.Where(entry => entry.Users > users.Count * DominantPhraseShare)
			.Select(static entry => entry.Group)
			.FirstOrDefault();

		if (dominant is null)
		{
			return (users.Count, null);
		}

		HashSet<string> others = [.. messages.Where(message => !string.Equals(message.Phrase, dominant.Key, StringComparison.Ordinal)).Select(static message => message.User)];
		int dominantOnly = dominant.Select(static message => message.User).Distinct(StringComparer.Ordinal).Count(user => !others.Contains(user));
		return (others.Count + Math.Min(dominantOnly, DominantPhraseLimit), dominant.Key);
	}

	private static double[] ComputeBaselines(double[] scores, int radius)
	{
		double[] baselines = new double[scores.Length];
		List<double> window = new(radius * 2 + 1);

		for (int i = 0; i < scores.Length; ++i)
		{
			int from = Math.Max(0, i - radius);
			int to = Math.Min(scores.Length - 1, i + radius);
			window.Clear();

			for (int j = from; j <= to; ++j)
			{
				window.Add(scores[j]);
			}

			window.Sort();
			int middle = window.Count / 2;
			baselines[i] = window.Count % 2 is 1 ? window[middle] : (window[middle - 1] + window[middle]) / 2;
		}

		return baselines;
	}

	// 判为抽奖口令或复读的话不用，否则章节名会是抽奖口令
	private static string FindPhrase(ReadOnlySpan<List<Message>?> bins, HashSet<string> excluded, int maxLength)
	{
		Dictionary<string, (HashSet<string> Users, string Text, TimeSpan First)> phrases = new(StringComparer.Ordinal);

		foreach (List<Message>? messages in bins)
		{
			foreach (Message message in messages ?? [])
			{
				if (excluded.Contains(message.Phrase))
				{
					continue;
				}

				string key = CollapseRepeats(message.Phrase);

				if (!phrases.TryGetValue(key, out (HashSet<string> Users, string Text, TimeSpan First) entry))
				{
					entry = ([], message.Text.Trim(), message.Time);
					phrases.Add(key, entry);
				}

				entry.Users.Add(message.User);
			}
		}

		IEnumerable<KeyValuePair<string, (HashSet<string> Users, string Text, TimeSpan First)>> ranked = phrases
			.OrderBy(pair => new StringInfo(pair.Key).LengthInTextElements > maxLength)
			.ThenByDescending(static pair => pair.Value.Users.Count)
			.ThenBy(static pair => pair.Value.First)
			.ThenBy(static pair => pair.Key, StringComparer.Ordinal);

		return ranked.FirstOrDefault() is { Value.Text: { } text } ? Truncate(text, maxLength) : string.Empty;
	}

	// 昵称打码时 uid 的哈希仍是真的
	private static string GetUserKey(DanmakuRecord.Comment comment, int index)
	{
		return comment.UidHash is not @"0" ? comment.UidHash : string.Create(CultureInfo.InvariantCulture, $@"#{index}");
	}

	// 不合并“哈哈哈”和“哈哈哈哈”，各自发的不同说法不算复读
	private static string Normalize(string text)
	{
		StringBuilder builder = new(text.Length);
		bool space = false;

		foreach (char c in text.Trim())
		{
			if (char.IsWhiteSpace(c))
			{
				space = true;
				continue;
			}

			if (space)
			{
				builder.Append(' ');
				space = false;
			}

			builder.Append(char.ToLowerInvariant(c));
		}

		return builder.ToString();
	}

	// 选代表短语时把“哈哈哈哈哈”和“哈哈哈”算作一句
	private static string CollapseRepeats(string phrase)
	{
		StringBuilder builder = new(phrase.Length);
		Rune previous = default;
		int run = 0;

		foreach (Rune rune in phrase.EnumerateRunes())
		{
			run = rune == previous ? run + 1 : 1;
			previous = rune;

			if (run <= 3)
			{
				builder.Append(rune.ToString());
			}
		}

		return builder.ToString();
	}

	private static string Truncate(string text, int maxLength)
	{
		StringInfo info = new(text);
		return info.LengthInTextElements > maxLength ? info.SubstringByTextElements(0, maxLength) : text;
	}

	private sealed record Message(string User, string Phrase, string Text, TimeSpan Time);
}
