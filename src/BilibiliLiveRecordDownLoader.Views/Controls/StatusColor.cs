using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;

namespace BilibiliLiveRecordDownLoader.Views.Controls;

// 前景色取自 Fluent2 主题资源，切换主题时跟着更新
public static partial class StatusColor
{
	static StatusColor()
	{
		ValueProperty.Changed.AddClassHandler<Control>(OnValueChanged);
	}

	[GeneratedAttachedProperty]
	public static partial object? GetValue(Control control);

	private static string? GetResourceKey(object? status)
	{
		return status switch
		{
			LiveStatus.Live or BackgroundTaskStatus.Completed or NotificationType.Success or DanmakuConnectionState.Connected => "SystemFillColorSuccessBrush",
			LiveStatus.Round or RecordStatus.Connecting or BackgroundTaskStatus.Running or LogLevel.Warning or NotificationType.Warning or DanmakuConnectionState.Connecting => "SystemFillColorCautionBrush",
			RecordStatus.Recording or BackgroundTaskStatus.Failed or LogLevel.Error or LogLevel.Critical or NotificationType.Error or DanmakuConnectionState.Disconnected => "SystemFillColorCriticalBrush",
			NotificationType.Information or LogLevel.Information => "AccentTextFillColorPrimaryBrush",
			LiveStatus or RecordStatus or BackgroundTaskStatus or LogLevel => "TextFillColorSecondaryBrush",
			_ => null
		};
	}

	private static void OnValueChanged(Control control, AvaloniaPropertyChangedEventArgs e)
	{
		AvaloniaProperty property = control is Shape ? Shape.FillProperty : TextElement.ForegroundProperty;

		if (GetResourceKey(e.NewValue) is { } key)
		{
			control.Bind(property, control.GetResourceObservable(key));
		}
		else
		{
			control.ClearValue(property);
		}
	}
}
