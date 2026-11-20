namespace BilibiliLiveRecordDownLoader.Views.Views;

public partial class MainWindow : Window
{
	public MainWindow()
	{
		InitializeComponent();
	}

	protected override void OnClosing(WindowClosingEventArgs e)
	{
		if (DataContext is MainWindowViewModel viewModel && WindowState is WindowState.Normal)
		{
			viewModel.SaveWindowSize(ClientSize.Width, ClientSize.Height);
		}

		base.OnClosing(e);
	}
}
