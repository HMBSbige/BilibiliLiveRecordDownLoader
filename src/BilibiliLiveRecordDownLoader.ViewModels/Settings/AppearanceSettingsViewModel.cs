namespace BilibiliLiveRecordDownLoader.ViewModels.Settings;

public sealed partial class AppearanceSettingsViewModel(IAppConfigManager configManager, IThemeService themeService) : ViewModelBase
{
	public IReadOnlyList<AppTheme> ThemeOptions { get; } = Enum.GetValues<AppTheme>();

	// 语言名为空的一项表示跟随系统
	public ObservableCollection<LanguageOptionViewModel> Languages { get; } =
	[
		new(string.Empty, string.Empty),
		.. AppLanguage.SupportedLanguageTags.Select(static name => CultureInfo.GetCultureInfo(name)).Select(static culture => new LanguageOptionViewModel(culture.Name, culture.NativeName))
	];

	[Reactive]
	public partial AppTheme Theme { get; set; }

	[Reactive]
	public partial LanguageOptionViewModel? SelectedLanguage { get; set; }

	internal void ApplyConfig(AppConfig config)
	{
		SelectedLanguage = Languages.FirstOrDefault(language => language.CultureName == config.Language) ?? Languages[0];
		Theme = config.Theme;

		PersistToConfig(configManager, this.WhenChanged(static viewModel => viewModel.SelectedLanguage).Map(static language => language?.CultureName), static (appConfig, value) => appConfig.Language = value);
		PersistToConfig(configManager, this.WhenChanged(static viewModel => viewModel.Theme), static (appConfig, value) => appConfig.Theme = value);

		this.WhenChanged(static viewModel => viewModel.SelectedLanguage)
			.KeepNotNull()
			.Subscribe(static language => ApplyCulture(language.CultureName))
			.DisposeWith(Disposables);

		this.WhenChanged(static viewModel => viewModel.Theme)
			.Subscribe(themeService.Apply)
			.DisposeWith(Disposables);
	}

	private static void ApplyCulture(string cultureName)
	{
		AppLanguage.SetCurrent(string.IsNullOrEmpty(cultureName) ? CultureInfo.InstalledUICulture : CultureInfo.GetCultureInfo(cultureName));
	}
}
