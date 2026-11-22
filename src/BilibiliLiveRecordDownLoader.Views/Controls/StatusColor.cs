using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;

namespace BilibiliLiveRecordDownLoader.Views.Controls;

// 前景色取自 Semi 主题资源，切换主题时跟着更新
public sealed class StatusColor
{
	public static readonly AttachedProperty<object?> ValueProperty = AvaloniaProperty.RegisterAttached<StatusColor, Control, object?>("Value");

	static StatusColor()
	{
		ValueProperty.Changed.AddClassHandler<Control>(OnValueChanged);
	}

	private StatusColor()
	{
	}

	public static object? GetValue(Control control)
	{
		return control.GetValue(ValueProperty);
	}

	public static void SetValue(Control control, object? value)
	{
		control.SetValue(ValueProperty, value);
	}

	public static string? GetResourceKey(object? status)
	{
		return status switch
		{
			LiveStatus.Live or BackgroundTaskStatus.Completed or NotificationType.Success => "SemiColorSuccess",
			LiveStatus.Round or RecordStatus.Connecting or BackgroundTaskStatus.Running or LogLevel.Warning or NotificationType.Warning => "SemiColorWarning",
			RecordStatus.Recording or BackgroundTaskStatus.Failed or LogLevel.Error or LogLevel.Critical or NotificationType.Error => "SemiColorDanger",
			NotificationType.Information or LogLevel.Information => "SemiColorPrimary",
			LiveStatus or RecordStatus or BackgroundTaskStatus or LogLevel => "SemiColorText2",
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
