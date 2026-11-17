namespace BilibiliLiveRecordDownLoader.ViewModels.Settings;

public sealed partial class NetworkSettingsViewModel : ViewModelBase
{
	private bool _isInitialized;

	[Reactive]
	public partial string? UserAgent { get; set; }

	[Reactive]
	public partial bool UseSystemProxy { get; set; }

	internal void ApplyConfig(AppConfig config)
	{
		if (_isInitialized)
		{
			return;
		}

		UserAgent = config.UserAgent;
		UseSystemProxy = config.UseSystemProxy;

		PersistToConfig(this.WhenChanged(static viewModel => viewModel.UserAgent), static (appConfig, value) => appConfig.UserAgent = string.IsNullOrWhiteSpace(value) ? null : value.Trim());
		PersistToConfig(this.WhenChanged(static viewModel => viewModel.UseSystemProxy), static (appConfig, value) => appConfig.UseSystemProxy = value);

		this.WhenChanged(static viewModel => viewModel.UseSystemProxy)
			.Skip(1)
			.Subscribe(static enabled => AppLocator.Current.GetRequiredService<IAppLifecycleService>().SetUseSystemProxy(enabled))
			.DisposeWith(Disposables);

		_isInitialized = true;
	}
}
