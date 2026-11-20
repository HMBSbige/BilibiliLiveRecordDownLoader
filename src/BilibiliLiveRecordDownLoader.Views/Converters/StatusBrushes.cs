namespace BilibiliLiveRecordDownLoader.Views.Converters;

public static class StatusBrushes
{
	public static IBrush Positive { get; } = new ImmutableSolidColorBrush(Color.FromRgb(0x3B, 0xB3, 0x46));

	public static IBrush Warning { get; } = new ImmutableSolidColorBrush(Color.FromRgb(0xFC, 0x82, 0x00));

	public static IBrush Danger { get; } = new ImmutableSolidColorBrush(Color.FromRgb(0xF9, 0x39, 0x20));

	public static IBrush Info { get; } = new ImmutableSolidColorBrush(Color.FromRgb(0x00, 0x64, 0xFA));

	public static IBrush Neutral { get; } = new ImmutableSolidColorBrush(Color.FromRgb(0x88, 0x8D, 0x92));
}
