namespace BilibiliLiveRecordDownLoader.Views.Views;

public partial class MainView : UserControl
{
	public MainView()
	{
		InitializeComponent();
	}

	// Ctrl+点击会取消选中当前项，导航要始终停留在某个页面
	private void OnNavigationSelectionChanged(object? sender, SelectionChangedEventArgs e)
	{
		if (sender is ListBox { SelectedIndex: < 0 } navigation && e.RemovedItems is [{ } previous, ..])
		{
			navigation.SelectedItem = previous;
		}
	}
}
