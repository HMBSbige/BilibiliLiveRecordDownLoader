namespace BilibiliLiveRecordDownLoader.Domain.Shared.Localization;

[LocalizedConfiguration
(
	Default = "en",
	EnsureKeysIdentical = true,
	GenerationMode = GenerationMode.Compiled,
	NotificationMode = NotificationMode.CurrentCulturePropertyChanged
)]
public static partial class AppLanguage
{
	public static event EventHandler? CurrentChanged;

	public static void SetCurrent(CultureInfo culture)
	{
		CultureInfo.DefaultThreadCurrentCulture = culture;
		CultureInfo.DefaultThreadCurrentUICulture = culture;
		CultureInfo.CurrentCulture = culture;
		CultureInfo.CurrentUICulture = culture;
		SetCurrent(culture.Name);
		CurrentChanged?.Invoke(null, EventArgs.Empty);
	}
}
