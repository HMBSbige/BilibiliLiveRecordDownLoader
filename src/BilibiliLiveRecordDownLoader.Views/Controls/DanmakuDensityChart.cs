using Avalonia.Input;
using System.Windows.Input;

namespace BilibiliLiveRecordDownLoader.Views.Controls;

public sealed partial class DanmakuDensityChart : Control
{
	private const double PeakRadius = 3;

	// 高能区间正是曲线最高处，色带画在曲线下面会被填充盖住
	private const double HighlightOpacity = 0.25;

	static DanmakuDensityChart()
	{
		AffectsRender<DanmakuDensityChart>(DensityProperty, HighlightsProperty, DurationProperty, PositionProperty, FillProperty, StrokeProperty, HighlightBrushProperty, PeakBrushProperty, PositionBrushProperty);
		CursorProperty.OverrideDefaultValue<DanmakuDensityChart>(new Cursor(StandardCursorType.Hand));
	}

	[GeneratedStyledProperty]
	public partial DanmakuDensityDto? Density { get; set; }

	[GeneratedStyledProperty]
	public partial IReadOnlyList<DanmakuHighlightViewModel>? Highlights { get; set; }

	[GeneratedStyledProperty]
	public partial TimeSpan Duration { get; set; }

	[GeneratedStyledProperty]
	public partial TimeSpan? Position { get; set; }

	// 参数是文件内的 TimeSpan
	[GeneratedStyledProperty]
	public partial ICommand? Command { get; set; }

	[GeneratedStyledProperty]
	public partial IBrush? Fill { get; set; }

	[GeneratedStyledProperty]
	public partial IBrush? Stroke { get; set; }

	[GeneratedStyledProperty]
	public partial IBrush? HighlightBrush { get; set; }

	[GeneratedStyledProperty]
	public partial IBrush? PeakBrush { get; set; }

	[GeneratedStyledProperty]
	public partial IBrush? PositionBrush { get; set; }

	public override void Render(DrawingContext context)
	{
		Size size = Bounds.Size;
		TimeSpan duration = Duration;

		// 点击要落在控件上，背景透明也得画出来
		context.FillRectangle(Brushes.Transparent, new Rect(size));

		if (duration <= TimeSpan.Zero || size.Width <= 0 || size.Height <= 0)
		{
			return;
		}

		if (Density is { Counts.Count: > 0 } density && BuildCurve(density, duration, size) is { } curve)
		{
			context.DrawGeometry(Fill, Stroke is { } stroke ? new Pen(stroke) : null, curve);
		}

		if (HighlightBrush is { } highlightBrush)
		{
			using (context.PushOpacity(HighlightOpacity))
			{
				foreach (DanmakuHighlightViewModel highlight in Highlights ?? [])
				{
					double left = ToX(highlight.Start, duration, size.Width);
					double right = Math.Max(ToX(highlight.End, duration, size.Width), left + 2);
					context.FillRectangle(highlightBrush, new Rect(left, 0, right - left, size.Height));
				}
			}
		}

		foreach (DanmakuHighlightViewModel highlight in Highlights ?? [])
		{
			context.DrawEllipse(PeakBrush, null, new Point(ToX(highlight.Peak, duration, size.Width), PeakRadius + 1), PeakRadius, PeakRadius);
		}

		if (Position is { } position && PositionBrush is { } positionBrush)
		{
			double x = ToX(position, duration, size.Width);
			context.DrawLine(new Pen(positionBrush, 1.5), new Point(x, 0), new Point(x, size.Height));
		}
	}

	protected override void OnPointerPressed(PointerPressedEventArgs e)
	{
		base.OnPointerPressed(e);

		if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || Bounds.Width <= 0 || Command is not { } command)
		{
			return;
		}

		double ratio = Math.Clamp(e.GetPosition(this).X / Bounds.Width, 0, 1);
		TimeSpan time = TimeSpan.FromTicks((long)(Duration.Ticks * ratio));

		if (command.CanExecute(time))
		{
			e.Handled = true;
			command.Execute(time);
		}
	}

	private static double ToX(TimeSpan time, TimeSpan duration, double width)
	{
		return Math.Clamp(time.Ticks / (double)duration.Ticks, 0, 1) * width;
	}

	// 每列取覆盖格子的最大值，格子比像素多时尖峰才不会被抹平
	private static StreamGeometry? BuildCurve(DanmakuDensityDto density, TimeSpan duration, Size size)
	{
		IReadOnlyList<int> counts = density.Counts;
		int max = counts.Max();

		if (max <= 0 || density.BinWidth <= TimeSpan.Zero)
		{
			return null;
		}

		int columns = Math.Max(1, (int)Math.Ceiling(size.Width));
		double binsPerColumn = duration.Ticks / (double)density.BinWidth.Ticks / columns;
		const double top = PeakRadius * 2 + 2;
		StreamGeometry geometry = new();

		using StreamGeometryContext builder = geometry.Open();
		builder.BeginFigure(new Point(0, size.Height));

		for (int column = 0; column < columns; ++column)
		{
			int first = (int)(column * binsPerColumn);
			int last = Math.Max(first, (int)Math.Ceiling((column + 1) * binsPerColumn) - 1);
			int value = 0;

			for (int bin = first; bin <= last && bin < counts.Count; ++bin)
			{
				value = Math.Max(value, counts[bin]);
			}

			double y = size.Height - value / (double)max * (size.Height - top);
			builder.LineTo(new Point(Math.Min(column + 0.5, size.Width), y));
		}

		builder.LineTo(new Point(size.Width, size.Height));
		builder.EndFigure(true);

		return geometry;
	}
}
