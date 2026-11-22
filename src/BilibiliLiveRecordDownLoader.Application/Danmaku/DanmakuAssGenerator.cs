using System.Text;

namespace BilibiliLiveRecordDownLoader.Application.Danmaku;

internal readonly record struct DanmakuAssResult(int Shown, int Dropped, int MessagesShown, int MessagesDropped);

// 消息栏里的文字格式由调用方按界面语言给出；传入的昵称和礼物名已经转义。Guard 的参数是昵称、等级（1 总督、2 提督、3 舰长）、月数和是否续费，Gift 的参数是昵称、礼物名和数量
internal sealed record DanmakuAssLabels(Func<string, int, int, bool, string> Guard, Func<string, string, int, string> Gift);

// 固定参数：短边 1080 时字号 38、滚动 12 秒、固定 5 秒、不透明度 180、无描边、阴影 1
internal static class DanmakuAssGenerator
{
	private const int DefaultWidth = 1920;
	private const int DefaultHeight = 1080;
	// 按短边缩放，竖屏时字不会跟着高度放大到挤占画面
	private const double ReferenceSize = 1080;
	private const double ReferenceFontSize = 38;
	// B 站弹幕的标准字号，其他字号按它的比例放大缩小
	private const double StandardDanmakuSize = 25;
	private const string FontName = @"Microsoft YaHei";
	// ASS 的透明度，0 不透明；255 - 180
	private const string Alpha = @"4B";

	private static readonly TimeSpan ScrollDuration = TimeSpan.FromSeconds(12);
	private static readonly TimeSpan FixedDuration = TimeSpan.FromSeconds(5);
	private static readonly UTF8Encoding Utf8WithBom = new(true);

	// 很多播放器只在有 BOM 时把 ASS 当 UTF-8，覆盖已有文件以便调整偏移后重新生成；messageLabels 为 null 时不画消息栏
	public static async Task<DanmakuAssResult> WriteFileAsync(string path, IEnumerable<DanmakuRecord> records, VideoResolution? videoSize, DanmakuAssLabels? messageLabels, CancellationToken cancellationToken)
	{
		StringWriter writer = new(CultureInfo.InvariantCulture);
		DanmakuAssResult result = Write(writer, records, videoSize, messageLabels);
		await File.WriteAllTextAsync(path, writer.ToString(), Utf8WithBom, cancellationToken);
		return result;
	}

	// 弹幕在同一轨道上互不重叠，所有轨道都放不下时丢弃；SC、上舰和付费礼物画在左下角的消息栏里
	public static DanmakuAssResult Write(TextWriter writer, IEnumerable<DanmakuRecord> records, VideoResolution? videoSize, DanmakuAssLabels? messageLabels)
	{
		(int width, int height) = videoSize is { Width: > 0, Height: > 0 } size ? (size.Width, size.Height) : (DefaultWidth, DefaultHeight);
		double scale = Math.Min(width, height) / ReferenceSize;
		int laneHeight = Math.Max(1, (int)Math.Round(ReferenceFontSize * scale));
		int laneCount = Math.Max(1, height / laneHeight);
		ScrollLane?[] scrollLanes = new ScrollLane?[laneCount];
		TimeSpan[] topLanes = new TimeSpan[laneCount];
		TimeSpan[] bottomLanes = new TimeSpan[laneCount];
		Array.Fill(topLanes, TimeSpan.MinValue);
		Array.Fill(bottomLanes, TimeSpan.MinValue);
		DanmakuRecord[] sorted = [.. records.OrderBy(static record => record.Time)];
		DanmakuAssMessages.Geometry bar = new(width, height, scale);
		DanmakuAssMessages.Rendering? messages = messageLabels is null ? null : DanmakuAssMessages.Prepare(sorted, bar, messageLabels);
		DanmakuAssMessages.Occupancy? occupied = messages?.Occupancy;

		WriteHeader(writer, width, height, laneHeight, bar.FontSize);

		int shown = 0;
		int dropped = 0;

		foreach (DanmakuRecord.Comment comment in sorted.OfType<DanmakuRecord.Comment>())
		{
			if (comment.Mode is not (>= 1 and <= 5))
			{
				continue;
			}

			string text = EscapeText(comment.Text);

			if (text.Length is 0)
			{
				continue;
			}

			int fontSize = Math.Max(1, (int)Math.Round(ReferenceFontSize * scale * (comment.FontSize > 0 ? comment.FontSize : StandardDanmakuSize) / StandardDanmakuSize));
			int span = Math.Clamp((fontSize + laneHeight - 1) / laneHeight, 1, laneCount);
			TimeSpan start = comment.Time < TimeSpan.Zero ? TimeSpan.Zero : comment.Time;
			double textWidth = EstimateWidth(text, fontSize);
			string? placement = comment.Mode switch
			{
				4 => PlaceFixed(bottomLanes, span, start, lane => string.Create(CultureInfo.InvariantCulture, $@"\an2\pos({width / 2},{height - lane * laneHeight})"), BottomAvoidance(occupied, start, width, height, textWidth, laneHeight)),
				5 => PlaceFixed(topLanes, span, start, lane => string.Create(CultureInfo.InvariantCulture, $@"\an8\pos({width / 2},{lane * laneHeight})"), null),
				_ => PlaceScroll(scrollLanes, span, start, textWidth, width, laneHeight)
			};

			if (placement is null)
			{
				++dropped;
				continue;
			}

			TimeSpan end = start + (comment.Mode is 4 or 5 ? FixedDuration : ScrollDuration);
			writer.Write(@"Dialogue: 0,");
			writer.Write(FormatTime(start));
			writer.Write(',');
			writer.Write(FormatTime(end));
			writer.Write(@",Danmaku,,0,0,0,,{");
			writer.Write(placement);

			if (fontSize != laneHeight)
			{
				writer.Write(string.Create(CultureInfo.InvariantCulture, $@"\fs{fontSize}"));
			}

			if ((comment.Color & 0xFFFFFF) is not 0xFFFFFF)
			{
				writer.Write(@"\c&H");
				writer.Write(ToAssColor(comment.Color));
				writer.Write('&');
			}

			writer.Write('}');
			writer.Write(text);
			writer.Write('\n');
			++shown;
		}

		foreach (string line in messages?.Lines ?? [])
		{
			writer.Write(line);
		}

		return new DanmakuAssResult(shown, dropped, messages?.Shown ?? 0, messages?.Dropped ?? 0);
	}

	// 底部弹幕从下往上排，最先用到的几条轨道正好挨着消息栏；居中的弹幕要足够宽才伸进消息栏的横向范围。
	// 滚动和顶部弹幕从上往下排，和消息栏重叠的轨道总在最下面，只有上面的都不空闲时才会用到，那时退回重叠的排法也一样，所以不用判断
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

	// 从上往下找第一段连续空闲的轨道；新弹幕出现时上一条的尾部必须已经进入屏幕，并且在上一条离开屏幕之前追不上它
	private static string? PlaceScroll(ScrollLane?[] lanes, int span, TimeSpan start, double textWidth, int screenWidth, int laneHeight)
	{
		double speed = (screenWidth + textWidth) / ScrollDuration.TotalSeconds;

		if (FindLane(lanes.Length, span, lane => lanes[lane] is not { } previous || previous.AllowsNext(start, speed, screenWidth), null) is not { } found)
		{
			return null;
		}

		ScrollLane occupied = new(start, textWidth, speed);
		Array.Fill(lanes, occupied, found, span);
		int y = found * laneHeight;
		return string.Create(CultureInfo.InvariantCulture, $@"\an7\move({screenWidth},{y},{-(int)Math.Ceiling(textWidth)},{y})");
	}

	private static string? PlaceFixed(TimeSpan[] lanes, int span, TimeSpan start, Func<int, string> position, Func<int, int, bool>? avoid)
	{
		if (FindLane(lanes.Length, span, lane => lanes[lane] <= start, avoid) is not { } found)
		{
			return null;
		}

		Array.Fill(lanes, start + FixedDuration, found, span);
		return position(found);
	}

	// 第一段连续空闲、并且不和消息栏重叠的轨道；不重叠的都不空闲时退回第一段空闲的，宁可重叠也不多丢弹幕
	private static int? FindLane(int laneCount, int span, Func<int, bool> isFree, Func<int, int, bool>? avoid)
	{
		int? fallback = null;

		for (int lane = 0; lane + span <= laneCount; ++lane)
		{
			bool free = true;

			for (int i = lane; i < lane + span && free; ++i)
			{
				free = isFree(i);
			}

			if (!free)
			{
				continue;
			}

			if (avoid is null || !avoid(lane, span))
			{
				return lane;
			}

			fallback ??= lane;
		}

		return fallback;
	}

	// 没有字体度量，按全角一个字号、半角 0.6 个字号估算；宁可估宽一点留出空隙，也不要重叠
	internal static double EstimateWidth(string text, int fontSize)
	{
		double width = 0;

		foreach (Rune rune in text.EnumerateRunes())
		{
			width += IsWide(rune.Value) ? fontSize : fontSize * 0.6;
		}

		return width;
	}

	internal static bool IsWide(int codePoint)
	{
		return codePoint is >= 0x1100 and <= 0x115F
			or >= 0x2E80 and <= 0xA4CF
			or >= 0xAC00 and <= 0xD7A3
			or >= 0xF900 and <= 0xFAFF
			or >= 0xFE30 and <= 0xFE4F
			or >= 0xFF00 and <= 0xFF60
			or >= 0xFFE0 and <= 0xFFE6
			or >= 0x1F000;
	}

	// ASS 里 { } 开始和结束样式代码，反斜杠和后面的 N、n、h 组成换行和硬空格，换成全角字符原样显示；弹幕只占一行，换行换成空格
	internal static string EscapeText(string text)
	{
		StringBuilder builder = new(text.Length);

		foreach (Rune rune in text.EnumerateRunes())
		{
			switch (rune.Value)
			{
				case '{':
					builder.Append('｛');
					break;
				case '}':
					builder.Append('｝');
					break;
				case '\\':
					builder.Append('＼');
					break;
				case '\n' or '\r' or '\t':
					builder.Append(' ');
					break;
				default:
				{
					if (!Rune.IsControl(rune))
					{
						builder.Append(rune.ToString());
					}

					break;
				}
			}
		}

		return builder.ToString().Trim();
	}

	// ASS 的颜色是 BGR
	internal static string ToAssColor(int rgb)
	{
		return string.Create(CultureInfo.InvariantCulture, $@"{rgb & 0xFF:X2}{(rgb >> 8) & 0xFF:X2}{(rgb >> 16) & 0xFF:X2}");
	}

	// ASS 的时间精度是百分之一秒
	internal static string FormatTime(TimeSpan value)
	{
		long centiseconds = value.Ticks / (TimeSpan.TicksPerMillisecond * 10);
		return string.Create(CultureInfo.InvariantCulture, $@"{centiseconds / 360000}:{centiseconds / 6000 % 60:00}:{centiseconds / 100 % 60:00}.{centiseconds % 100:00}");
	}

	private sealed record ScrollLane(TimeSpan Start, double Width, double Speed)
	{
		public bool AllowsNext(TimeSpan start, double speed, int screenWidth)
		{
			double elapsed = (start - Start).TotalSeconds;

			// 上一条的尾部还没有完全进入屏幕
			if (elapsed * Speed < Width)
			{
				return false;
			}

			// 新弹幕在上一条离开屏幕时走过的距离不能超过屏幕宽度
			double remaining = ScrollDuration.TotalSeconds - elapsed;
			return remaining <= 0 || remaining * speed <= screenWidth;
		}
	}
}
