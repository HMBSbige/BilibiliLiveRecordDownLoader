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

	// 要在 SetCurrent 改掉当前区域性之前取；生成的部分带静态构造函数，第一次调用 SetCurrent 时会先初始化
	public static CultureInfo SystemCulture { get; } = CultureInfo.CurrentCulture;

	public static void SetCurrent(CultureInfo uiCulture, CultureInfo formatCulture)
	{
		CultureInfo.DefaultThreadCurrentCulture = formatCulture;
		CultureInfo.DefaultThreadCurrentUICulture = uiCulture;
		CultureInfo.CurrentCulture = formatCulture;
		CultureInfo.CurrentUICulture = uiCulture;
		SetCurrent(uiCulture.Name);
		CurrentChanged?.Invoke(null, EventArgs.Empty);
	}
}
