namespace BilibiliLiveRecordDownLoader.ViewModels.Settings;

public sealed partial class RecordingSettingsViewModel(IAppConfigManager configManager) : ViewModelBase
{
	// 数字框每按一个键就改一次值；录制中途就会读到分段设置，不能把输到一半的数存进去
	internal static readonly TimeSpan NumberInputQuietPeriod = TimeSpan.FromSeconds(1);

	private bool _isInitialized;

	public IReadOnlyList<ChoiceOption> RecorderTypeOptions { get; } = ChoiceOption.OfAll<RecorderType>();

	public IReadOnlyList<ChoiceOption> HostSelectionOptions { get; } = ChoiceOption.OfAll<StreamHostSelection>();

	public IReadOnlyList<ChoiceOption> SplitModeOptions { get; } = ChoiceOption.OfAll<RecordingSplitMode>();

	public PriorityListViewModel CodecPriority { get; } = new();

	public PriorityListViewModel FormatPriority { get; } = new();

	[Reactive]
	public partial ChoiceOption RecorderType { get; set; } = new(Domain.Shared.Recording.RecorderType.BuiltIn);

	[Reactive]
	public partial ChoiceOption HostSelection { get; set; } = new(StreamHostSelection.FirstResponse);

	[Reactive]
	public partial bool ConvertToMp4 { get; set; }

	[Reactive]
	public partial bool DeleteAfterConvert { get; set; }

	[Reactive]
	public partial ChoiceOption SplitMode { get; set; } = new(RecordingSplitMode.None);

	[Reactive]
	public partial decimal SplitDurationMinutes { get; set; }

	[Reactive]
	public partial decimal SplitSizeMegabytes { get; set; }

	[Reactive]
	public partial bool SplitOnTitleChange { get; set; }

	[Reactive]
	public partial bool IsSplitByDuration { get; private set; }

	[Reactive]
	public partial bool IsSplitBySize { get; private set; }

	internal void ApplyConfig(AppConfig config)
	{
		if (_isInitialized)
		{
			return;
		}

		RecorderType = new ChoiceOption(config.RecorderType);
		HostSelection = new ChoiceOption(config.StreamHostSelection);
		ConvertToMp4 = config.ConvertToMp4;
		DeleteAfterConvert = config.DeleteAfterConvert;
		SplitMode = new ChoiceOption(config.SplitMode);
		SplitDurationMinutes = (decimal)config.SplitDuration.TotalMinutes;
		SplitSizeMegabytes = config.SplitSizeMegabytes;
		SplitOnTitleChange = config.SplitOnTitleChange;
		CodecPriority.Reset(config.CodecPriority);
		FormatPriority.Reset(config.FormatPriority);

		this.WhenChanged(static viewModel => viewModel.SplitMode)
			.Subscribe
			(mode =>
				{
					IsSplitByDuration = mode.As<RecordingSplitMode>() is RecordingSplitMode.Duration;
					IsSplitBySize = mode.As<RecordingSplitMode>() is RecordingSplitMode.Size;
				}
			)
			.DisposeWith(Disposables);

		PersistToConfig(configManager, this.WhenChanged(static viewModel => viewModel.RecorderType), static (appConfig, value) => appConfig.RecorderType = value.As<RecorderType>() ?? appConfig.RecorderType);
		PersistToConfig(configManager, this.WhenChanged(static viewModel => viewModel.HostSelection), static (appConfig, value) => appConfig.StreamHostSelection = value.As<StreamHostSelection>() ?? appConfig.StreamHostSelection);
		PersistToConfig(configManager, this.WhenChanged(static viewModel => viewModel.ConvertToMp4), static (appConfig, value) => appConfig.ConvertToMp4 = value);
		PersistToConfig(configManager, this.WhenChanged(static viewModel => viewModel.DeleteAfterConvert), static (appConfig, value) => appConfig.DeleteAfterConvert = value);
		PersistToConfig(configManager, this.WhenChanged(static viewModel => viewModel.SplitMode), static (appConfig, value) => appConfig.SplitMode = value.As<RecordingSplitMode>() ?? appConfig.SplitMode);
		PersistToConfig(configManager, this.WhenChanged(static viewModel => viewModel.SplitDurationMinutes), static (appConfig, value) => appConfig.SplitDuration = TimeSpan.FromMinutes((double)value), NumberInputQuietPeriod);
		PersistToConfig(configManager, this.WhenChanged(static viewModel => viewModel.SplitSizeMegabytes), static (appConfig, value) => appConfig.SplitSizeMegabytes = (long)value, NumberInputQuietPeriod);
		PersistToConfig(configManager, this.WhenChanged(static viewModel => viewModel.SplitOnTitleChange), static (appConfig, value) => appConfig.SplitOnTitleChange = value);
		PersistToConfig(configManager, CodecPriority.WhenChanged(static list => list.Values), (appConfig, _) => appConfig.CodecPriority = CodecPriority.ToList<StreamCodec>());
		PersistToConfig(configManager, FormatPriority.WhenChanged(static list => list.Values), (appConfig, _) => appConfig.FormatPriority = FormatPriority.ToList<StreamFormat>());

		_isInitialized = true;
	}
}
