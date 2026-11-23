using Avalonia.Controls.Metadata;

namespace BilibiliLiveRecordDownLoader.Views.Controls;

// 标题和说明在左，控件在右；加 stacked 类时控件换到说明下面，没有标题和说明时就占满整行
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
