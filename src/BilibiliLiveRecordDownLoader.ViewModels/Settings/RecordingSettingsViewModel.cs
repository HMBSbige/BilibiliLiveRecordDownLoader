namespace BilibiliLiveRecordDownLoader.ViewModels.Settings;

public sealed partial class RecordingSettingsViewModel(IAppConfigManager configManager) : ViewModelBase
{
	// 数字框每按一个键就改一次值，录制中途会读分段设置，不能存输到一半的数
	internal static readonly TimeSpan NumberInputQuietPeriod = TimeSpan.FromSeconds(1);

	private bool _isInitialized;

	public IReadOnlyList<RecorderType> RecorderTypeOptions { get; } = Enum.GetValues<RecorderType>();

	public IReadOnlyList<StreamHostSelection> HostSelectionOptions { get; } = Enum.GetValues<StreamHostSelection>();

	public IReadOnlyList<RecordingSplitMode> SplitModeOptions { get; } = Enum.GetValues<RecordingSplitMode>();

	public PriorityListViewModel CodecPriority { get; } = new();

	public PriorityListViewModel FormatPriority { get; } = new();

	[Reactive]
	public partial RecorderType RecorderType { get; set; } = RecorderType.BuiltIn;

	[Reactive]
	public partial StreamHostSelection HostSelection { get; set; } = StreamHostSelection.FirstResponse;

	[Reactive]
	public partial bool ConvertToMp4 { get; set; }

	[Reactive]
	public partial bool DeleteAfterConvert { get; set; }

	[Reactive]
	public partial RecordingSplitMode SplitMode { get; set; } = RecordingSplitMode.None;

	[Reactive]
	public partial decimal SplitDurationMinutes { get; set; }

	[Reactive]
	public partial decimal SplitSizeMegabytes { get; set; }

	[Reactive]
	public partial bool SplitOnTitleChange { get; set; }

	[Reactive]
	public partial bool IncludeTitleInFileName { get; set; }

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

		RecorderType = config.RecorderType;
		HostSelection = config.StreamHostSelection;
		ConvertToMp4 = config.ConvertToMp4;
		DeleteAfterConvert = config.DeleteAfterConvert;
		SplitMode = config.SplitMode;
		SplitDurationMinutes = (decimal)config.SplitDuration.TotalMinutes;
		SplitSizeMegabytes = config.SplitSizeMegabytes;
		SplitOnTitleChange = config.SplitOnTitleChange;
		IncludeTitleInFileName = config.IncludeTitleInFileName;
		CodecPriority.Reset(config.CodecPriority);
		FormatPriority.Reset(config.FormatPriority);

		this.WhenChanged(static viewModel => viewModel.SplitMode)
			.Subscribe
			(mode =>
				{
					IsSplitByDuration = mode is RecordingSplitMode.Duration;
					IsSplitBySize = mode is RecordingSplitMode.Size;
				}
			)
			.DisposeWith(Disposables);

		PersistToConfig(configManager, this.WhenChanged(static viewModel => viewModel.RecorderType), static (appConfig, value) => appConfig.RecorderType = value);
		PersistToConfig(configManager, this.WhenChanged(static viewModel => viewModel.HostSelection), static (appConfig, value) => appConfig.StreamHostSelection = value);
		PersistToConfig(configManager, this.WhenChanged(static viewModel => viewModel.ConvertToMp4), static (appConfig, value) => appConfig.ConvertToMp4 = value);
		PersistToConfig(configManager, this.WhenChanged(static viewModel => viewModel.DeleteAfterConvert), static (appConfig, value) => appConfig.DeleteAfterConvert = value);
		PersistToConfig(configManager, this.WhenChanged(static viewModel => viewModel.SplitMode), static (appConfig, value) => appConfig.SplitMode = value);
		PersistToConfig(configManager, this.WhenChanged(static viewModel => viewModel.SplitDurationMinutes), static (appConfig, value) => appConfig.SplitDuration = TimeSpan.FromMinutes((double)value), NumberInputQuietPeriod);
		PersistToConfig(configManager, this.WhenChanged(static viewModel => viewModel.SplitSizeMegabytes), static (appConfig, value) => appConfig.SplitSizeMegabytes = (long)value, NumberInputQuietPeriod);
		PersistToConfig(configManager, this.WhenChanged(static viewModel => viewModel.SplitOnTitleChange), static (appConfig, value) => appConfig.SplitOnTitleChange = value);
		PersistToConfig(configManager, this.WhenChanged(static viewModel => viewModel.IncludeTitleInFileName), static (appConfig, value) => appConfig.IncludeTitleInFileName = value);
		// 保存可能在线程池上执行，用发出的快照，不去读界面正在改的列表
		PersistToConfig(configManager, CodecPriority.WhenChanged(static list => list.Values), static (appConfig, values) => appConfig.CodecPriority = [.. values.OfType<StreamCodec>()]);
		PersistToConfig(configManager, FormatPriority.WhenChanged(static list => list.Values), static (appConfig, values) => appConfig.FormatPriority = [.. values.OfType<StreamFormat>()]);

		_isInitialized = true;
	}
}
