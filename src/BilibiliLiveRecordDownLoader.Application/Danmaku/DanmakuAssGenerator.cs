using System.Text;

namespace BilibiliLiveRecordDownLoader.Application.Danmaku;

internal readonly record struct DanmakuAssResult(int Shown, int Dropped, int MessagesShown, int MessagesDropped);

// 传入的昵称和礼物名已经转义；Guard 的参数是昵称、等级、月数和是否续费，Gift 的参数是昵称、礼物名和数量
internal sealed record DanmakuAssLabels(Func<string, GuardLevel, int, bool, string> Guard, Func<string, string, int, string> Gift);

internal static class DanmakuAssGenerator
{
	private const int DefaultWidth = 1920;
	private const int DefaultHeight = 1080;
	// 按短边缩放，竖屏时字不会跟着高度放大
	private const double ReferenceSize = 1080;
	private const double ReferenceFontSize = 38;
	// B 站弹幕的标准字号
	private const double StandardDanmakuSize = 25;
	private const string FontName = @"Microsoft YaHei";
	// ASS 的透明度，0 不透明；255 - 180
	private const string Alpha = @"4B";

	private static readonly TimeSpan ScrollDuration = TimeSpan.FromSeconds(12);
	private static readonly TimeSpan FixedDuration = TimeSpan.FromSeconds(5);
	private static readonly UTF8Encoding Utf8WithBom = new(true);

	// 很多播放器只在有 BOM 时把 ASS 当 UTF-8
	// 不接受取消，写到一半停下会留下残缺的字幕
	public static async Task WriteFileAsync(string path, IEnumerable<DanmakuRecord> records, VideoResolution? videoSize, DanmakuAssLabels? messageLabels)
	{
		await using StreamWriter writer = new(path, false, Utf8WithBom);
		Write(writer, records, videoSize, messageLabels);
	}

	public static DanmakuAssResult Write(TextWriter writer, IEnumerable<DanmakuRecord> records, VideoResolution? videoSize, DanmakuAssLabels? messageLabels)
	{
		(int width, int height) = videoSize is { Width: > 0, Height: > 0 } size ? (size.Width, size.Height) : (DefaultWidth, DefaultHeight);
		double scale = Math.Min(width, height) / ReferenceSize;
		int laneHeight = Math.Max(1, (int)Math.Round(ReferenceFontSize * scale));
		int laneCount = Math.Max(1, height / laneHeight);
		ScrollLane?[] scrollLanes = new ScrollLane?[laneCount];
		// 记的是轨道空出来的时刻，初值 0 即空闲
		TimeSpan[] topLanes = new TimeSpan[laneCount];
		TimeSpan[] bottomLanes = new TimeSpan[laneCount];
		DanmakuRecord[] sorted = [.. records.OrderBy(static record => record.Time)];
		DanmakuAssMessages.Geometry bar = new(width, height, scale);
		DanmakuAssMessages.Rendering? messages = messageLabels is null ? null : DanmakuAssMessages.Prepare(sorted, bar, messageLabels);

		WriteHeader(writer, width, height, laneHeight, bar.FontSize);

		int shown = 0;
		int dropped = 0;

		foreach (DanmakuRecord.Comment comment in sorted.OfType<DanmakuRecord.Comment>())
		{
			if (comment.Mode is not (>= 1 and <= 5) || DanmakuAssText.Escape(comment.Text) is not { Length: > 0 } text)
			{
				continue;
			}

			int fontSize = Math.Max(1, (int)Math.Round(ReferenceFontSize * scale * (comment.FontSize > 0 ? comment.FontSize : StandardDanmakuSize) / StandardDanmakuSize));
			int span = Math.Clamp((fontSize + laneHeight - 1) / laneHeight, 1, laneCount);
			TimeSpan start = comment.Time < TimeSpan.Zero ? TimeSpan.Zero : comment.Time;
			double textWidth = EstimateWidth(text, fontSize);
			string? position = comment.Mode switch
			{
				4 => PlaceFixed(bottomLanes, span, start, BottomAvoidance(messages?.Occupancy, start, width, height, textWidth, laneHeight)) is { } lane ? string.Create(CultureInfo.InvariantCulture, $@"\an2\pos({width / 2},{height - lane * laneHeight})") : null,
				5 => PlaceFixed(topLanes, span, start, null) is { } lane ? string.Create(CultureInfo.InvariantCulture, $@"\an8\pos({width / 2},{lane * laneHeight})") : null,
				_ => PlaceScroll(scrollLanes, span, start, textWidth, width) is { } lane ? string.Create(CultureInfo.InvariantCulture, $@"\an7\move({width},{lane * laneHeight},{-(int)Math.Ceiling(textWidth)},{lane * laneHeight})") : null
			};

			if (position is null)
			{
				++dropped;
				continue;
			}

			TimeSpan end = start + (comment.Mode is 4 or 5 ? FixedDuration : ScrollDuration);
			string sizeTag = fontSize == laneHeight ? string.Empty : string.Create(CultureInfo.InvariantCulture, $@"\fs{fontSize}");
			string colorTag = (comment.Color & 0xFFFFFF) is 0xFFFFFF ? string.Empty : $@"\c&H{DanmakuAssText.Color(comment.Color)}&";
			writer.Write($"Dialogue: 0,{DanmakuAssText.Time(start)},{DanmakuAssText.Time(end)},Danmaku,,0,0,0,,{{{position}{sizeTag}{colorTag}}}{text}\n");
			++shown;
		}

		foreach (string line in messages?.Lines ?? [])
		{
			writer.Write(line);
		}

		return new DanmakuAssResult(shown, dropped, messages?.Shown ?? 0, messages?.Dropped ?? 0);
	}

	// 只有底部弹幕要避让：滚动和顶部弹幕从上往下排，和消息栏重叠的轨道只在上面的都不空闲时才用到，那时退回重叠的排法也一样
	private static Func<int, int, bool>? BottomAvoidance(DanmakuAssMessages.Occupancy? occupied, TimeSpan start, int screenWidth, int screenHeight, double textWidth, int laneHeight)
	{
		if (occupied is null || screenWidth / 2.0 - textWidth / 2 >= occupied.Right || occupied.TopDuring(start, start + FixedDuration) is not { } top)
		{
			return null;
		}

		return (lane, count) => screenHeight - lane * laneHeight > top && screenHeight - (lane + count) * laneHeight < occupied.Bottom;
	}

	private static void WriteHeader(TextWriter writer, int width, int height, int fontSize, int messageFontSize)
	{
		writer.Write(string.Create(CultureInfo.InvariantCulture, $"""
			[Script Info]
			ScriptType: v4.00+
			PlayResX: {width}
			PlayResY: {height}
			WrapStyle: 2
			ScaledBorderAndShadow: yes
			YCbCr Matrix: None

			[V4+ Styles]
			Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
			Style: Danmaku,{FontName},{fontSize},&H{Alpha}FFFFFF,&H{Alpha}FFFFFF,&H{Alpha}000000,&H{Alpha}000000,0,0,0,0,100,100,0,0,1,0,1,7,0,0,0,1
			Style: Message,{FontName},{messageFontSize},&H00FFFFFF,&H00FFFFFF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,0,0,7,0,0,0,1

			[Events]
			Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text

			""").ReplaceLineEndings("\n"));
	}

	private static int? PlaceScroll(ScrollLane?[] lanes, int span, TimeSpan start, double textWidth, int screenWidth)
	{
		double speed = (screenWidth + textWidth) / ScrollDuration.TotalSeconds;

		if (FindLane(lanes.Length, span, lane => lanes[lane] is not { } previous || previous.AllowsNext(start, speed, screenWidth), null) is not { } found)
		{
			return null;
		}

		Array.Fill(lanes, new ScrollLane(start, textWidth, speed), found, span);
		return found;
	}

	private static int? PlaceFixed(TimeSpan[] lanes, int span, TimeSpan start, Func<int, int, bool>? avoid)
	{
		if (FindLane(lanes.Length, span, lane => lanes[lane] <= start, avoid) is not { } found)
		{
			return null;
		}

		Array.Fill(lanes, start + FixedDuration, found, span);
		return found;
	}

	// 不重叠的都不空闲时退回第一段空闲的，宁可和消息栏重叠也不多丢弹幕
	private static int? FindLane(int laneCount, int span, Func<int, bool> isFree, Func<int, int, bool>? avoid)
	{
		int? fallback = null;

	outer:
		for (int lane = 0; lane + span <= laneCount; ++lane)
		{
			for (int i = lane; i < lane + span; ++i)
			{
				if (!isFree(i))
				{
					continue outer;
				}
			}

			if (avoid is null || !avoid(lane, span))
			{
				return lane;
			}

			fallback ??= lane;
		}

		return fallback;
	}

	// 没有字体度量，宁可估宽一点也不要重叠
	private static double EstimateWidth(string text, int fontSize)
	{
		double width = 0;

		foreach (Rune rune in text.EnumerateRunes())
		{
			width += DanmakuAssText.IsWide(rune.Value) ? fontSize : fontSize * 0.6;
		}

		return width;
	}

	private readonly record struct ScrollLane(TimeSpan Start, double Width, double Speed)
	{
		public bool AllowsNext(TimeSpan start, double speed, int screenWidth)
		{
			double elapsed = (start - Start).TotalSeconds;

			if (elapsed * Speed < Width)
			{
				return false;
			}

			double remaining = ScrollDuration.TotalSeconds - elapsed;
			return remaining <= 0 || remaining * speed <= screenWidth;
		}
	}
}
