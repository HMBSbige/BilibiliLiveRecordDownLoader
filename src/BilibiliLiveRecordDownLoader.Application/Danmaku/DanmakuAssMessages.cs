using System.Diagnostics;
using System.Text;

namespace BilibiliLiveRecordDownLoader.Application.Danmaku;

internal static class DanmakuAssMessages
{
	private const int MaxBodyLines = 5;
	private const int BackgroundLayer = 1;
	private const int TextLayer = 2;
	private const string Ellipsis = @"…";
	private const int NameColor = 0x333333;
	// 底色不透明：其他轨道都占满时普通弹幕会落到消息栏的高度，半透明会让两层文字叠在一起
	private const string CardAlpha = @"00";
	private const int GiftBackground = 0x202020;
	// 贝塞尔曲线近似四分之一圆时，控制点离端点的距离与半径之比
	private const double CircleControl = 0.5523;

	private static readonly TimeSpan GuardDuration = TimeSpan.FromSeconds(6);
	private static readonly TimeSpan SuperChatMinimum = TimeSpan.FromSeconds(8);
	private static readonly TimeSpan SuperChatMaximum = TimeSpan.FromSeconds(20);
	private static readonly TimeSpan SuperChatPerCharacter = TimeSpan.FromSeconds(0.15);

	// 尺寸是短边 1080 时的像素，按短边缩放
	internal sealed record Geometry(int Width, int Height, double Scale)
	{
		public double Left => Width * 0.02;

		public double Bottom => Height * 0.96;

		public double BarWidth => Math.Min(480 * Scale, Width * 0.4);

		public double MaxHeight => Height * 0.45;

		public double Gap => 8 * Scale;

		public int FontSize => Math.Max(1, (int)Math.Round(30 * Scale));

		public double HeaderHeight => 48 * Scale;

		public double LineHeight => 36 * Scale;

		public double BodyPadding => 10 * Scale;

		public double Padding => 16 * Scale;

		public double CapsuleHeight => 46 * Scale;

		public double CapsulePadding => 20 * Scale;

		public double Radius => 6 * Scale;
	}

	private closed record Card(double Height)
	{
		public sealed record SuperChat(double Height, string Name, string Price, IReadOnlyList<string> Lines, SuperChatTier Tier) : Card(Height);

		// Text 的参数是连击数量
		public sealed record Capsule(double Height, int Color, Func<int, string> Text) : Card(Height);
	}

	internal sealed record Rendering(IReadOnlyList<string> Lines, int Shown, int Dropped, Occupancy Occupancy);

	// sorted 要按时间排好；要先于普通弹幕算，普通弹幕排位置时要知道消息栏占着哪里
	public static Rendering Prepare(IReadOnlyList<DanmakuRecord> sorted, Geometry bar, DanmakuAssLabels labels)
	{
		List<Card> cards = [];
		List<DanmakuMessageRequest> requests = [];
		double textWidth = bar.BarWidth - 2 * bar.CapsulePadding;
		HashSet<long> deleted = [.. sorted.OfType<DanmakuRecord.SuperChatDeletion>().SelectMany(static deletion => deletion.Ids)];

		foreach (DanmakuRecord record in sorted)
		{
			TimeSpan time = Quantize(record.Time);

			switch (record)
			{
				case DanmakuRecord.SuperChat superChat when superChat.Id is 0 || !deleted.Contains(superChat.Id):
				{
					string text = DanmakuAssText.Escape(superChat.Text);
					Card.SuperChat card = CreateSuperChat(DanmakuAssText.Escape(superChat.User), text, superChat.Price, bar);
					cards.Add(card);
					requests.Add(new DanmakuMessageRequest(time, DanmakuMessageKind.SuperChat, card.Height, SuperChatDuration(text, superChat.Duration), 1, null));
					break;
				}
				// 等级可能是没定义的值或坏数据
				case DanmakuRecord.Guard { Level: GuardLevel.Governor or GuardLevel.Admiral or GuardLevel.Captain } guard:
				{
					string user = DanmakuAssText.Escape(guard.User);
					string text = FitName(name => labels.Guard(name, guard.Level, guard.Count, guard.IsRenewal), user, bar.FontSize, textWidth);
					cards.Add(new Card.Capsule(bar.CapsuleHeight, GuardColor(guard.Level), _ => text));
					requests.Add(new DanmakuMessageRequest(time, DanmakuMessageKind.Guard, bar.CapsuleHeight, GuardDuration, 1, null));
					break;
				}
				case DanmakuRecord.Gift { Price: > 0 } gift:
				{
					string user = DanmakuAssText.Escape(gift.User);
					string giftName = DanmakuAssText.Escape(gift.Name);
					// 昵称打码时 uid 为 0
					string comboKey = gift.Uid is not 0 ? string.Create(CultureInfo.InvariantCulture, $"{gift.Uid}\n{gift.Name}") : $"\n{gift.User}\n{gift.Name}";
					cards.Add(new Card.Capsule(bar.CapsuleHeight, GiftBackground, count => FitName(name => labels.Gift(name, giftName, count), user, bar.FontSize, textWidth)));
					requests.Add(new DanmakuMessageRequest(time, DanmakuMessageKind.Gift, bar.CapsuleHeight, DanmakuMessageBar.ComboWindow, Math.Max(1, gift.Count), comboKey));
					break;
				}
			}
		}

		DanmakuMessageLayout layout = DanmakuMessageBar.Layout(requests, bar.MaxHeight, bar.Gap);
		List<(TimeSpan Start, string Line)> lines = [];
		List<(TimeSpan Start, TimeSpan End, double Extent)> extents = [];

		foreach (DanmakuMessagePlacement placement in layout.Placements)
		{
			Card card = cards[placement.Request];

			foreach (DanmakuMessageSpan span in placement.Spans)
			{
				Render(lines, bar, card, placement, span);
				extents.Add((span.Start, span.End, Math.Max(span.From, span.To) + card.Height));
			}
		}

		return new Rendering([.. lines.OrderBy(static line => line.Start).Select(static line => line.Line)], layout.Shown, layout.Dropped, new Occupancy(bar, extents));
	}

	internal sealed class Occupancy
	{
		// 按时刻排好，_heights[i] 是从 _times[i] 到下一个时刻的高度
		private readonly TimeSpan[] _times;
		private readonly double[] _heights;

		public Occupancy(Geometry bar, IReadOnlyList<(TimeSpan Start, TimeSpan End, double Extent)> extents)
		{
			Right = bar.Left + bar.BarWidth;
			Bottom = bar.Bottom;

			List<(TimeSpan Time, double Extent, bool Adds)> events = [.. extents.Select(static extent => (extent.Start, extent.Extent, true)), .. extents.Select(static extent => (extent.End, extent.Extent, false))];
			events.Sort(static (a, b) => a.Time.CompareTo(b.Time));
			SortedDictionary<double, int> active = [];
			List<TimeSpan> times = [];
			List<double> heights = [];

			for (int i = 0; i < events.Count;)
			{
				TimeSpan time = events[i].Time;

				for (; i < events.Count && events[i].Time == time; ++i)
				{
					(_, double extent, bool adds) = events[i];
					int count = active.GetValueOrDefault(extent) + (adds ? 1 : -1);

					if (count is 0)
					{
						active.Remove(extent);
					}
					else
					{
						active[extent] = count;
					}
				}

				times.Add(time);
				heights.Add(active.Count is 0 ? 0 : active.Keys.Last());
			}

			_times = [.. times];
			_heights = [.. heights];
		}

		public double Right { get; }

		public double Bottom { get; }

		// [from, to) 期间消息栏顶端的纵坐标；这段时间里消息栏是空的时为 null
		public double? TopDuring(TimeSpan from, TimeSpan to)
		{
			int index = Array.BinarySearch(_times, from);
			index = index >= 0 ? index : Math.Max(0, ~index - 1);
			double height = 0;

			for (; index < _times.Length && _times[index] < to; ++index)
			{
				height = Math.Max(height, _heights[index]);
			}

			return height > 0 ? Bottom - height : null;
		}
	}

	// user 和 text 已经转义
	private static Card.SuperChat CreateSuperChat(string user, string text, long price, Geometry bar)
	{
		string yuan = FormatYuan(price);
		string name = Truncate(user, bar.FontSize, bar.BarWidth - 3 * bar.Padding - Measure(yuan, bar.FontSize));
		IReadOnlyList<string> body = Wrap(text, bar.FontSize, bar.BarWidth - 2 * bar.Padding, MaxBodyLines);
		double height = bar.HeaderHeight + (body.Count is 0 ? 0 : body.Count * bar.LineHeight + 2 * bar.BodyPadding);
		return new Card.SuperChat(height, name, yuan, body, SuperChatTier.For(price));
	}

	private static TimeSpan SuperChatDuration(string text, TimeSpan pinned)
	{
		TimeSpan duration = SuperChatMinimum + SuperChatPerCharacter * text.EnumerateRunes().Count();
		duration = duration < SuperChatMaximum ? duration : SuperChatMaximum;
		return pinned > TimeSpan.Zero && pinned < duration ? Quantize(pinned) : duration;
	}

	private static int GuardColor(GuardLevel level)
	{
		return level switch
		{
			GuardLevel.Governor => 0xE0A030,
			GuardLevel.Admiral => 0x8C4DE6,
			GuardLevel.Captain => 0x3E7BFA,
			GuardLevel.None => throw new UnreachableException()
		};
	}

	private static void Render(List<(TimeSpan Start, string Line)> lines, Geometry bar, Card card, DanmakuMessagePlacement placement, DanmakuMessageSpan span)
	{
		int height = Round(card.Height);
		Frame frame = new
		(
			Round(bar.Left),
			Round(bar.Bottom - span.From - card.Height),
			Round(bar.Bottom - span.To - card.Height),
			Milliseconds(span.MoveEnd - span.Start)
		);
		string fade = Fade(placement, span);

		switch (card)
		{
			case Card.SuperChat superChat:
			{
				int width = Round(bar.BarWidth);
				int radius = Round(bar.Radius);
				int header = Round(bar.HeaderHeight);
				int center = header / 2;

				Add(BackgroundLayer, $@"\an7{frame.At(0, 0)}{fade}\1c&H{DanmakuAssText.Color(superChat.Tier.Background)}&\1a&H{CardAlpha}&\p1", RoundedRectangle(width, header, radius, superChat.Lines.Count is 0 ? radius : 0));

				if (superChat.Lines.Count > 0)
				{
					Add(BackgroundLayer, $@"\an7{frame.At(0, header)}{fade}\1c&H{DanmakuAssText.Color(superChat.Tier.BottomBackground)}&\1a&H{CardAlpha}&\p1", RoundedRectangle(width, height - header, 0, radius));
					// 每行单独定位：\N 的行距取决于字体和渲染器，和按行高算出的卡片高度对不上
					for (int i = 0; i < superChat.Lines.Count; ++i)
					{
						Add(TextLayer, $@"\an4{frame.At(bar.Padding, header + bar.BodyPadding + bar.LineHeight * (i + 0.5))}{fade}\1c&H{DanmakuAssText.Color(superChat.Tier.TextColor)}&", superChat.Lines[i]);
					}
				}

				Add(TextLayer, $@"\an4{frame.At(bar.Padding, center)}{fade}\b1\1c&H{DanmakuAssText.Color(NameColor)}&", superChat.Name);
				Add(TextLayer, $@"\an6{frame.At(bar.BarWidth - bar.Padding, center)}{fade}\1c&H{DanmakuAssText.Color(superChat.Tier.PriceColor)}&", superChat.Price);
				break;
			}
			case Card.Capsule capsule:
			{
				string text = capsule.Text(span.Count);
				int width = Round(Math.Min(bar.BarWidth, Measure(text, bar.FontSize) + 2 * bar.CapsulePadding));
				Add(BackgroundLayer, $@"\an7{frame.At(0, 0)}{fade}\1c&H{DanmakuAssText.Color(capsule.Color)}&\1a&H{CardAlpha}&\p1", RoundedRectangle(Math.Max(width, height), height, height / 2, height / 2));
				Add(TextLayer, $@"\an4{frame.At(bar.CapsulePadding, height / 2.0)}{fade}", text);
				break;
			}
			default:
			{
				throw new UnreachableException();
			}
		}

		void Add(int layer, string tags, string text)
		{
			lines.Add((span.Start, string.Create(CultureInfo.InvariantCulture, $"Dialogue: {layer},{DanmakuAssText.Time(span.Start)},{DanmakuAssText.Time(span.End)},Message,,0,0,0,,{{{tags}}}{text}\n")));
		}
	}

	// 开始或结束在淡入淡出途中时 \fad 会从头开始变，改用 \fade 从当时的透明度接着变
	private static string Fade(DanmakuMessagePlacement placement, DanmakuMessageSpan span)
	{
		TimeSpan fadeInEnd = placement.ShownAt + DanmakuMessageBar.Transition;
		bool fadesIn = span.Start < fadeInEnd;
		bool fadesOut = span.End > placement.FadeOutStart;

		if ((!fadesIn || span.Start == placement.ShownAt) && (!fadesOut || span.End == placement.RemovedAt && span.Start <= placement.FadeOutStart))
		{
			if (!fadesIn && !fadesOut)
			{
				return string.Empty;
			}

			int fadeIn = fadesIn ? Milliseconds(fadeInEnd - placement.ShownAt) : 0;
			int fadeOut = fadesOut ? Milliseconds(placement.RemovedAt - placement.FadeOutStart) : 0;
			return string.Create(CultureInfo.InvariantCulture, $@"\fad({fadeIn},{fadeOut})");
		}

		int alpha = AlphaAt(placement, span.Start);
		int end = Milliseconds(placement.RemovedAt - span.Start);

		if (span.Start >= placement.FadeOutStart)
		{
			return string.Create(CultureInfo.InvariantCulture, $@"\fade({alpha},{alpha},255,0,0,0,{end})");
		}

		int shown = Math.Max(0, Milliseconds(fadeInEnd - span.Start));
		int fading = Math.Max(shown, Milliseconds(placement.FadeOutStart - span.Start));
		return string.Create(CultureInfo.InvariantCulture, $@"\fade({alpha},0,255,0,{shown},{fading},{end})");
	}

	private static int AlphaAt(DanmakuMessagePlacement placement, TimeSpan time)
	{
		TimeSpan fadeInEnd = placement.ShownAt + DanmakuMessageBar.Transition;

		if (time < fadeInEnd)
		{
			return (int)Math.Round(255 * (1 - (time - placement.ShownAt) / DanmakuMessageBar.Transition));
		}

		if (time > placement.FadeOutStart && placement.RemovedAt > placement.FadeOutStart)
		{
			return (int)Math.Round(255 * ((time - placement.FadeOutStart) / (placement.RemovedAt - placement.FadeOutStart)));
		}

		return 0;
	}

	// 左上角是原点，配合 \an7 定位
	private static string RoundedRectangle(int width, int height, int topRadius, int bottomRadius)
	{
		StringBuilder builder = new();
		builder.Append(CultureInfo.InvariantCulture, $@"m {topRadius} 0 l {width - topRadius} 0");

		if (topRadius > 0)
		{
			Curve(width - topRadius + topRadius * CircleControl, 0, width, topRadius - topRadius * CircleControl, width, topRadius);
		}

		builder.Append(CultureInfo.InvariantCulture, $@" l {width} {height - bottomRadius}");

		if (bottomRadius > 0)
		{
			Curve(width, height - bottomRadius + bottomRadius * CircleControl, width - bottomRadius + bottomRadius * CircleControl, height, width - bottomRadius, height);
		}

		builder.Append(CultureInfo.InvariantCulture, $@" l {bottomRadius} {height}");

		if (bottomRadius > 0)
		{
			Curve(bottomRadius - bottomRadius * CircleControl, height, 0, height - bottomRadius + bottomRadius * CircleControl, 0, height - bottomRadius);
		}

		builder.Append(CultureInfo.InvariantCulture, $@" l 0 {topRadius}");

		if (topRadius > 0)
		{
			Curve(0, topRadius - topRadius * CircleControl, topRadius - topRadius * CircleControl, 0, topRadius, 0);
		}

		return builder.ToString();

		void Curve(double x1, double y1, double x2, double y2, double x3, double y3)
		{
			builder.Append(CultureInfo.InvariantCulture, $@" b {Round(x1)} {Round(y1)} {Round(x2)} {Round(y2)} {Round(x3)} {Round(y3)}");
		}
	}

	internal static IReadOnlyList<string> Wrap(string text, double fontSize, double maxWidth, int maxLines)
	{
		List<string> lines = [];
		StringBuilder line = new();
		double width = 0;
		int breakAt = -1;

		foreach (Rune rune in text.EnumerateRunes())
		{
			double runeWidth = Measure(rune, fontSize);

			if (width + runeWidth > maxWidth && line.Length > 0)
			{
				if (lines.Count + 1 >= maxLines)
				{
					lines.Add(Ellipsize(line.ToString(), fontSize, maxWidth));
					return lines;
				}

				string rest = string.Empty;

				if (rune.Value is not ' ' && !DanmakuAssText.IsWide(rune.Value) && breakAt > 0 && breakAt < line.Length)
				{
					rest = line.ToString(breakAt, line.Length - breakAt);
					line.Length = breakAt;
				}

				lines.Add(line.ToString().TrimEnd());
				line.Clear().Append(rest);
				width = Measure(rest, fontSize);
				breakAt = -1;
			}

			if (rune.Value is ' ' && line.Length is 0)
			{
				continue;
			}

			line.Append(rune);
			width += runeWidth;

			if (rune.Value is ' ' || DanmakuAssText.IsWide(rune.Value))
			{
				breakAt = line.Length;
			}
		}

		if (line.Length > 0)
		{
			lines.Add(line.ToString().TrimEnd());
		}

		return lines;
	}

	// 常见的 ASS 渲染器把字号当成行高，微软雅黑的全角字宽约是字号的 0.76，取 0.8 留一点余量
	// 不沿用滚动弹幕按一个字号估的做法，那样卡片里的文字只占到七成多宽度
	internal static double Measure(Rune rune, double fontSize)
	{
		double em = fontSize * 0.8;

		if (DanmakuAssText.IsWide(rune.Value))
		{
			return em;
		}

		return em * rune.Value switch
		{
			' ' or 'i' or 'j' or 'l' or 'I' or '.' or ',' or ':' or ';' or '\'' or '!' or '|' => 0.3,
			'f' or 't' or 'r' or '(' or ')' or '[' or ']' or '-' => 0.4,
			'm' or 'w' => 0.85,
			'M' or 'W' => 0.95,
			>= 'A' and <= 'Z' => 0.7,
			_ => 0.6
		};
	}

	internal static double Measure(string text, double fontSize)
	{
		double width = 0;

		foreach (Rune rune in text.EnumerateRunes())
		{
			width += Measure(rune, fontSize);
		}

		return width;
	}

	internal static string Truncate(string text, double fontSize, double maxWidth)
	{
		return Measure(text, fontSize) <= maxWidth ? text : Ellipsize(text, fontSize, maxWidth);
	}

	// 省略号按全角估算，多数中文字体里它占一个字宽
	private static string Ellipsize(string text, double fontSize, double maxWidth)
	{
		ReadOnlySpan<char> kept = text;
		double width = Measure(text, fontSize);
		double ellipsis = fontSize * 0.8;

		while (!kept.IsEmpty && width + ellipsis > maxWidth)
		{
			Rune.DecodeLastFromUtf16(kept, out Rune last, out int length);
			width -= Measure(last, fontSize);
			kept = kept.Slice(0, kept.Length - length);
		}

		return string.Concat(kept.TrimEnd(), Ellipsis);
	}

	private static string FitName(Func<string, string> compose, string name, double fontSize, double maxWidth)
	{
		string full = compose(name);

		if (Measure(full, fontSize) <= maxWidth)
		{
			return full;
		}

		string fitted = Truncate(name, fontSize, maxWidth - Measure(compose(string.Empty), fontSize));
		return Truncate(compose(fitted), fontSize, maxWidth);
	}

	internal static string FormatYuan(long price)
	{
		return @"¥" + decimal.Round(price / 1000m, 2, MidpointRounding.AwayFromZero).ToString(@"0.##", CultureInfo.InvariantCulture);
	}

	// ASS 的时间精度是百分之一秒，先对齐，分段边界和移动、淡入淡出的毫秒数才对得上
	private static TimeSpan Quantize(TimeSpan time)
	{
		const long step = TimeSpan.TicksPerMillisecond * 10;
		return TimeSpan.FromTicks(Math.Max(0, time.Ticks) / step * step);
	}

	private static int Milliseconds(TimeSpan value)
	{
		return (int)Math.Round(value.TotalMilliseconds);
	}

	private static int Round(double value)
	{
		return (int)Math.Round(value, MidpointRounding.AwayFromZero);
	}

	// 位置先取整再加各部分的偏移，SC 上下两半才不会因为各自取整出现缝隙
	private readonly record struct Frame(int X, int FromTop, int ToTop, int MoveMilliseconds)
	{
		public string At(double dx, double dy)
		{
			int x = X + Round(dx);
			return MoveMilliseconds > 0 && FromTop != ToTop
				? string.Create(CultureInfo.InvariantCulture, $@"\move({x},{FromTop + Round(dy)},{x},{ToTop + Round(dy)},0,{MoveMilliseconds})")
				: string.Create(CultureInfo.InvariantCulture, $@"\pos({x},{ToTop + Round(dy)})");
		}
	}
}
