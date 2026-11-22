namespace BilibiliLiveRecordDownLoader.ViewModels.Settings;

public sealed partial class AppearanceSettingsViewModel : ViewModelBase
{
	private bool _isInitialized;

	public IReadOnlyList<ChoiceOption> ThemeOptions { get; } = ChoiceOption.OfAll<AppTheme>();

	public ObservableCollection<LanguageOptionViewModel> Languages { get; } = [];

	[Reactive]
	public partial ChoiceOption Theme { get; set; } = new(AppTheme.FollowSystem);

	[Reactive]
	public partial LanguageOptionViewModel? SelectedLanguage { get; set; }

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

		_isInitialized = true;
	}

	private static void ApplyCulture(string cultureName)
	{
		AppLanguage.SetCurrent(string.IsNullOrEmpty(cultureName) ? CultureInfo.InstalledUICulture : CultureInfo.GetCultureInfo(cultureName));
	}
}
