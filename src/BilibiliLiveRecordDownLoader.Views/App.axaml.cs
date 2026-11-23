namespace BilibiliLiveRecordDownLoader.Views;

public class App : Avalonia.Application
{
	public override void Initialize()
	{
		AvaloniaXamlLoader.Load(this);
		ApplyThemeStrings();
		AppLanguage.CurrentChanged += (_, _) => ApplyThemeStrings();
	}

	private void ApplyThemeStrings()
	{
		IResourceDictionary resources = Resources;
		resources["StringTextFlyoutCutText"] = AppLanguage.Current.CutText;
		resources["StringTextFlyoutCopyText"] = AppLanguage.Current.Copy;
		resources["StringTextFlyoutPasteText"] = AppLanguage.Current.PasteText;
		resources["StringScrollBarScrollHere"] = AppLanguage.Current.ScrollHere;
		resources["StringScrollBarTop"] = AppLanguage.Current.ScrollTop;
		resources["StringScrollBarBottom"] = AppLanguage.Current.ScrollBottom;
		resources["StringScrollBarPageUp"] = AppLanguage.Current.PageUp;
		resources["StringScrollBarPageDown"] = AppLanguage.Current.PageDown;
		resources["StringScrollBarScrollUp"] = AppLanguage.Current.ScrollUp;
		resources["StringScrollBarScrollDown"] = AppLanguage.Current.ScrollDown;
		resources["StringScrollBarLeftEdge"] = AppLanguage.Current.LeftEdge;
		resources["StringScrollBarRightEdge"] = AppLanguage.Current.RightEdge;
		resources["StringScrollBarPageLeft"] = AppLanguage.Current.PageLeft;
		resources["StringScrollBarPageRight"] = AppLanguage.Current.PageRight;
		resources["StringScrollBarScrollLeft"] = AppLanguage.Current.ScrollLeft;
		resources["StringScrollBarScrollRight"] = AppLanguage.Current.ScrollRight;
	}

	public override void OnFrameworkInitializationCompleted()
	{
		MainWindowViewModel viewModel = AppLocator.Current.GetRequiredService<MainWindowViewModel>();
		DataContext = viewModel;

		if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
		{
			desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
			AppLocator.Current.GetRequiredService<ApplicationShell>().Attach(this, desktop);
			Dispatcher.UIThread.Post(viewModel.RunStartupTasks, DispatcherPriority.Background);
		}

		base.OnFrameworkInitializationCompleted();
	}
}
