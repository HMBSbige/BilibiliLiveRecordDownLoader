using FluentIcons.Common;

namespace BilibiliLiveRecordDownLoader.Views.Controls;

public sealed partial class EmptyState : ContentControl
{
	[GeneratedStyledProperty]
	public partial Icon Icon { get; set; }

	[GeneratedStyledProperty]
	public partial string? Title { get; set; }

	[GeneratedStyledProperty]
	public partial string? Description { get; set; }
}
