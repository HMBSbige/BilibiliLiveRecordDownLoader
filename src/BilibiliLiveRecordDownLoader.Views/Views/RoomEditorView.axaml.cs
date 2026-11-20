using Avalonia.Interactivity;

namespace BilibiliLiveRecordDownLoader.Views.Views;

public partial class RoomEditorView : UserControl
{
	public RoomEditorView()
	{
		InitializeComponent();
	}

	protected override void OnLoaded(RoutedEventArgs e)
	{
		base.OnLoaded(e);

		if (RoomIdBox.IsEnabled)
		{
			RoomIdBox.Focus();
		}
	}
}
