namespace BilibiliLiveRecordDownLoader.ViewModels.Settings;

public sealed partial class AppearanceSettingsViewModel(IAppConfigManager configManager, IThemeService themeService, ISystemLanguageService systemLanguage) : ViewModelBase
{
	public IReadOnlyList<ChoiceOption> ThemeOptions { get; } = ChoiceOption.Of(Enum.GetValues<AppTheme>());

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

		Signal.Switch(this.WhenChanged(static viewModel => viewModel.SelectedLanguage).KeepNotNull().Map(ResolveCultures))
			.Subscribe(static cultures => AppLanguage.SetCurrent(cultures.Ui, cultures.Format))
			.DisposeWith(Disposables);

		this.WhenChanged(static viewModel => viewModel.Theme)
			.Subscribe(themeService.Apply)
			.DisposeWith(Disposables);
	}

	private IObservable<(CultureInfo Ui, CultureInfo Format)> ResolveCultures(LanguageOptionViewModel language)
	{
		if (string.IsNullOrEmpty(language.CultureName))
		{
			// 跟随系统时只换界面语言，数字和日期仍按系统的区域格式
			return systemLanguage.PreferredLanguage.Map(static tag => (ToCulture(tag), AppLanguage.SystemCulture));
		}

		CultureInfo culture = CultureInfo.GetCultureInfo(language.CultureName);
		return Signal.Return((culture, culture));
	}

	private static CultureInfo ToCulture(string languageTag)
	{
		if (languageTag.Length > 0)
		{
			try
			{
				return CultureInfo.GetCultureInfo(languageTag);
			}
			catch (CultureNotFoundException)
			{
				// .NET 不认识系统给的标签时退回区域格式
			}
		}

		return AppLanguage.SystemCulture;
	}
}
