using FluentIcons.Common;

namespace BilibiliLiveRecordDownLoader.Views.Controls;

public sealed class EmptyState : ContentControl
{
	public static readonly StyledProperty<Icon> IconProperty = AvaloniaProperty.Register<EmptyState, Icon>(nameof(Icon));

	public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<EmptyState, string?>(nameof(Title));

	public static readonly StyledProperty<string?> DescriptionProperty = AvaloniaProperty.Register<EmptyState, string?>(nameof(Description));

	public Icon Icon
	{
		get => GetValue(IconProperty);
		set => SetValue(IconProperty, value);
	}

	public string? Title
	{
		get => GetValue(TitleProperty);
		set => SetValue(TitleProperty, value);
	}

	public string? Description
	{
		get => GetValue(DescriptionProperty);
		set => SetValue(DescriptionProperty, value);
	}
}
