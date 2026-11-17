namespace BilibiliLiveRecordDownLoader.ViewModels.Settings;

public sealed partial class AppearanceSettingsViewModel : ViewModelBase
{
	private bool _isInitialized;
	private bool _isRevertingAutoStart;

	public AppearanceSettingsViewModel()
	{
		OpenConfigDirectoryCommand.DisposeWith(Disposables);
	}

	public IReadOnlyList<ChoiceOption> ThemeOptions { get; } = ChoiceOption.OfAll<AppTheme>();

	public ObservableCollection<LanguageOptionViewModel> Languages { get; } = [];

	public bool IsAutoStartSupported => AppLocator.Current.GetRequiredService<IAutoStartService>().IsSupported;

	[Reactive]
	public partial ChoiceOption Theme { get; set; } = new(AppTheme.FollowSystem);

	[Reactive]
	public partial LanguageOptionViewModel? SelectedLanguage { get; set; }

	[Reactive]
	public partial bool IsAutoStartEnabled { get; set; }

	internal void ApplyConfig(AppConfig config)
	{
		if (_isInitialized)
		{
			return;
		}

		Languages.Add(new LanguageOptionViewModel(string.Empty, string.Empty));

		foreach (string cultureName in AppLanguage.SupportedLanguageTags)
		{
			CultureInfo culture = CultureInfo.GetCultureInfo(cultureName);
			Languages.Add(new LanguageOptionViewModel(culture.Name, culture.NativeName));
		}

		SelectedLanguage = Languages.FirstOrDefault(language => language.CultureName == config.Language) ?? Languages[0];
		ApplyCulture(SelectedLanguage.CultureName);

		Theme = new ChoiceOption(config.Theme);
		AppLocator.Current.GetRequiredService<IThemeService>().Apply(config.Theme);

		IAutoStartService autoStartService = AppLocator.Current.GetRequiredService<IAutoStartService>();
		IsAutoStartEnabled = autoStartService.IsSupported && autoStartService.IsEnabled();

		PersistToConfig(this.WhenChanged(static viewModel => viewModel.SelectedLanguage).Map(static language => language?.CultureName), static (appConfig, value) => appConfig.Language = string.IsNullOrEmpty(value) ? null : value);
		PersistToConfig(this.WhenChanged(static viewModel => viewModel.Theme), static (appConfig, value) => appConfig.Theme = value.As<AppTheme>() ?? AppTheme.FollowSystem);

		this.WhenChanged(static viewModel => viewModel.SelectedLanguage)
			.Skip(1)
			.KeepNotNull()
			.Subscribe(static language => ApplyCulture(language.CultureName))
			.DisposeWith(Disposables);

		this.WhenChanged(static viewModel => viewModel.Theme)
			.Skip(1)
			.Subscribe(static theme => AppLocator.Current.GetRequiredService<IThemeService>().Apply(theme.As<AppTheme>() ?? AppTheme.FollowSystem))
			.DisposeWith(Disposables);

		this.WhenChanged(static viewModel => viewModel.IsAutoStartEnabled)
			.Skip(1)
			.Subscribe(enabled => ApplyAutoStart(autoStartService, enabled))
			.DisposeWith(Disposables);

		_isInitialized = true;
	}

	[ReactiveCommand]
	private async Task OpenConfigDirectoryAsync()
	{
		await AppLocator.Current.GetRequiredService<ILauncherService>().OpenFolderAsync(ConfigurationConsts.ConfigDirectory);
	}

	private void ApplyAutoStart(IAutoStartService autoStartService, bool enabled)
	{
		if (_isRevertingAutoStart)
		{
			return;
		}

		try
		{
			autoStartService.SetEnabled(enabled);
		}
		catch (Exception exception)
		{
			RxState.DefaultExceptionHandler.OnNext(exception);

			// 开关要反映实际状态
			_isRevertingAutoStart = true;

			try
			{
				IsAutoStartEnabled = autoStartService.IsEnabled();
			}
			finally
			{
				_isRevertingAutoStart = false;
			}
		}
	}

	private static void ApplyCulture(string cultureName)
	{
		AppLanguage.SetCurrent(string.IsNullOrEmpty(cultureName) ? CultureInfo.InstalledUICulture : CultureInfo.GetCultureInfo(cultureName));
	}
}
