using Avalonia.Controls.Metadata;

namespace BilibiliLiveRecordDownLoader.Views.Controls;

[PseudoClasses(":labeled")]
public sealed partial class SettingRow : ContentControl
{
	[GeneratedStyledProperty]
	public partial string? Header { get; set; }

	[GeneratedStyledProperty]
	public partial string? Description { get; set; }

	protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
	{
		base.OnPropertyChanged(change);

		if (change.Property == HeaderProperty || change.Property == DescriptionProperty)
		{
			PseudoClasses.Set(":labeled", !string.IsNullOrEmpty(Header) || !string.IsNullOrEmpty(Description));
		}
	}
}
