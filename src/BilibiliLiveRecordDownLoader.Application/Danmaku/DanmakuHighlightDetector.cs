using System.Text;

namespace BilibiliLiveRecordDownLoader.Application.Danmaku;

// Peak 是热度最高的那一格的开始；Phrase 在事件里只有抽奖口令或复读时为空
internal sealed record DanmakuHighlight(TimeSpan Start, TimeSpan Peak, TimeSpan End, int Score, string Phrase);

internal static class DanmakuHighlightDetector
{
	private const double BaselineRatio = 2.5;

	// 冷清的直播间基线接近 0，没有下限的话几个人同时说话就算高能
	private const int MinimumScore = 10;

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
		DanmakuRecord.Comment[] comments = [.. records.OfType<DanmakuRecord.Comment>().Where(static comment => !comment.IsStorm && comment.Time >= TimeSpan.Zero).OrderBy(static comment => comment.Time)];

		if (comments.Length is 0)
		{
			return [];
		}

		TimeSpan last = comments[^1].Time;
		TimeSpan total = duration is { } known && known > last ? known : last + BinWidth;
		int binCount = Math.Max((int)(last.Ticks / BinWidth.Ticks) + 1, (int)Math.Ceiling(total / BinWidth));
		List<Message>?[] bins = new List<Message>?[binCount];

		for (int i = 0; i < comments.Length; ++i)
		{
			DanmakuRecord.Comment comment = comments[i];
			int bin = (int)(comment.Time.Ticks / BinWidth.Ticks);
			(bins[bin] ??= []).Add(new Message(GetUserKey(comment, i), Normalize(comment.Text), comment.Text, comment.Time));
		}

		int[] scores = new int[binCount];
		string?[] dominantPhrases = new string?[binCount];

		for (int i = 0; i < binCount; ++i)
		{
			(scores[i], dominantPhrases[i]) = bins[i] is { } messages ? Score(messages) : (0, null);
		}

		double[] baselines = ComputeBaselines(scores, (int)(BaselineWindow.Ticks / BinWidth.Ticks));
		List<DanmakuHighlight> selected = [];
		int limit = Math.Max(1, (int)Math.Ceiling(total.TotalHours * MaxPerHour));

		foreach (DanmakuHighlight candidate in FindCandidates(bins, scores, dominantPhrases, baselines, total).OrderByDescending(static highlight => highlight.Score).ThenBy(static highlight => highlight.Start))
		{
			if (selected.Count >= limit)
			{
				break;
			}

			if (selected.TrueForAll(other => (other.Peak - candidate.Peak).Duration() >= MinimumSeparation))
			{
				selected.Add(candidate);
			}
		}

		return [.. selected.OrderBy(static highlight => highlight.Start)];
	}

	private static IEnumerable<DanmakuHighlight> FindCandidates(List<Message>?[] bins, int[] scores, string?[] dominantPhrases, double[] baselines, TimeSpan total)
	{
		for (int i = 0; i < scores.Length;)
		{
			if (!IsHot(i))
			{
				++i;
				continue;
			}

			int first = i;
			int peak = i;

			for (; i < scores.Length && IsHot(i); ++i)
			{
				if (scores[i] > scores[peak])
				{
					peak = i;
				}
			}

			TimeSpan end = BinWidth * i;
			HashSet<string> excluded = [with(StringComparer.Ordinal), .. dominantPhrases.Skip(first).Take(i - first).OfType<string>()];
			yield return new DanmakuHighlight(BinWidth * first, BinWidth * peak, end < total ? end : total, scores[peak], FindPhrase(bins.Skip(first).Take(i - first), excluded));
		}

		bool IsHot(int bin)
		{
			return scores[bin] >= MinimumScore && scores[bin] >= baselines[bin] * BaselineRatio;
		}
	}

	private static (int Score, string? Dominant) Score(List<Message> messages)
	{
		HashSet<string> users = [.. messages.Select(static message => message.User)];

		foreach (IGrouping<string, Message> phrase in messages.GroupBy(static message => message.Phrase, StringComparer.Ordinal))
		{
			HashSet<string> senders = [.. phrase.Select(static message => message.User)];

			if (senders.Count > users.Count * DominantPhraseShare)
			{
				HashSet<string> others = [.. messages.Where(message => !string.Equals(message.Phrase, phrase.Key, StringComparison.Ordinal)).Select(static message => message.User)];
				senders.ExceptWith(others);
				return (others.Count + Math.Min(senders.Count, DominantPhraseLimit), phrase.Key);
			}
		}

		return (users.Count, null);
	}

	private static double[] ComputeBaselines(int[] scores, int radius)
	{
		double[] baselines = new double[scores.Length];
		int[] buffer = new int[radius * 2 + 1];

		for (int i = 0; i < scores.Length; ++i)
		{
			int from = Math.Max(0, i - radius);
			int to = Math.Min(scores.Length - 1, i + radius);
			Span<int> window = buffer.AsSpan(0, to - from + 1);
			scores.AsSpan(from, window.Length).CopyTo(window);
			window.Sort();
			int middle = window.Length / 2;
			baselines[i] = window.Length % 2 is 1 ? window[middle] : (window[middle - 1] + window[middle]) / 2.0;
		}

		return baselines;
	}

	private static string FindPhrase(IEnumerable<List<Message>?> bins, HashSet<string> excluded)
	{
		string? text = bins
			.OfType<List<Message>>()
			.SelectMany(static messages => messages)
			.Where(message => !excluded.Contains(message.Phrase))
			.GroupBy(static message => CollapseRepeats(message.Phrase), StringComparer.Ordinal)
			.OrderBy(static phrase => new StringInfo(phrase.Key).LengthInTextElements > MaxPhraseLength)
			.ThenByDescending(static phrase => phrase.Select(static message => message.User).Distinct(StringComparer.Ordinal).Count())
			.ThenBy(static phrase => phrase.First().Time)
			.ThenBy(static phrase => phrase.Key, StringComparer.Ordinal)
			.Select(static phrase => phrase.First().Text.Trim())
			.FirstOrDefault();

		if (text is null)
		{
			return string.Empty;
		}

		StringInfo info = new(text);
		return info.LengthInTextElements > MaxPhraseLength ? info.SubstringByTextElements(0, MaxPhraseLength) : text;
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
				builder.Append(rune);
			}
		}

		return builder.ToString();
	}

	private sealed record Message(string User, string Phrase, string Text, TimeSpan Time);
}
