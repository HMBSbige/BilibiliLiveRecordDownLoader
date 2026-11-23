namespace BilibiliLiveRecordDownLoader.ViewModels.Settings;

public sealed partial class AppearanceSettingsViewModel(ISettingsAppService settingsAppService, IThemeService themeService, ISystemLanguageService systemLanguage) : ViewModelBase
{
	public IReadOnlyList<ChoiceOption> ThemeOptions { get; } = ChoiceOption.Of<AppTheme>();

	public IReadOnlyList<LanguageOptionViewModel> Languages { get; } =
	[
		new(string.Empty, string.Empty),
		.. AppLanguage.SupportedLanguageTags.Select(static name => CultureInfo.GetCultureInfo(name)).Select(static culture => new LanguageOptionViewModel(culture.Name, culture.NativeName))
	];

	[Reactive]
	public partial AppTheme Theme { get; set; }

	[Reactive]
	public partial LanguageOptionViewModel? SelectedLanguage { get; set; }

	internal void ApplySettings(AppSettingsDto settings)
	{
		SelectedLanguage = FindLanguage(settings.Language);
		Theme = settings.Theme;

		PersistSetting(settingsAppService, this.WhenChanged(static viewModel => viewModel.SelectedLanguage).Map(static language => language?.CultureName), static (current, value) => current with { Language = value });
		PersistSetting(settingsAppService, this.WhenChanged(static viewModel => viewModel.Theme), static (current, value) => current with { Theme = value });

		Signal.Switch(this.WhenChanged(static viewModel => viewModel.SelectedLanguage).KeepNotNull().Map(ResolveCultures))
			.Subscribe(static cultures => AppLanguage.SetCurrent(cultures.Ui, cultures.Format))
			.DisposeWith(Disposables);

		this.WhenChanged(static viewModel => viewModel.Theme)
			.Subscribe(themeService.Apply)
			.DisposeWith(Disposables);
	}

	internal LanguageOptionViewModel FindLanguage(string? cultureName)
	{
		return Languages.FirstOrDefault(language => string.Equals(language.CultureName, cultureName, StringComparison.OrdinalIgnoreCase)) ?? Languages[0];
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
			}
		}

		return AppLanguage.SystemCulture;
	}
}
