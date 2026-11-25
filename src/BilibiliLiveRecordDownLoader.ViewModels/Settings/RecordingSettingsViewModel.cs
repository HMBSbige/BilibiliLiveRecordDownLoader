namespace BilibiliLiveRecordDownLoader.ViewModels.Settings;

public sealed partial class RecordingSettingsViewModel(ISettingsAppService settingsAppService) : ViewModelBase
{
	// 数字框每按一个键就改一次值，录制中途会读分段设置，不能存输到一半的数
	internal static readonly TimeSpan NumberInputQuietPeriod = TimeSpan.FromSeconds(1);

	public IReadOnlyList<ChoiceOption> HostSelectionOptions { get; } = ChoiceOption.Of<StreamHostSelection>();

	public IReadOnlyList<ChoiceOption> SplitModeOptions { get; } = ChoiceOption.Of<RecordingSplitMode>();

	public PriorityListViewModel CodecPriority { get; } = new();

	public PriorityListViewModel FormatPriority { get; } = new();

	[Reactive]
	public partial StreamHostSelection HostSelection { get; set; }

	[Reactive]
	public partial bool ConvertToMp4 { get; set; }

	[Reactive]
	public partial bool DeleteAfterConvert { get; set; }

	[Reactive]
	public partial RecordingSplitMode SplitMode { get; set; }

	[Reactive]
	public partial decimal SplitDurationMinutes { get; set; }

	[Reactive]
	public partial decimal SplitSizeMebibytes { get; set; }

	[Reactive]
	public partial bool SplitOnTitleChange { get; set; }

	[Reactive]
	public partial bool IncludeTitleInFileName { get; set; }

	[Reactive]
	public partial bool IsSplitByDuration { get; private set; }

	[Reactive]
	public partial bool IsSplitBySize { get; private set; }

	internal void ApplySettings(AppSettingsDto settings)
	{
		HostSelection = settings.StreamHostSelection;
		ConvertToMp4 = settings.ConvertToMp4;
		DeleteAfterConvert = settings.DeleteAfterConvert;
		SplitMode = settings.SplitMode;
		SplitDurationMinutes = (decimal)settings.SplitDuration.TotalMinutes;
		SplitSizeMebibytes = settings.SplitSizeMebibytes;
		SplitOnTitleChange = settings.SplitOnTitleChange;
		IncludeTitleInFileName = settings.IncludeTitleInFileName;
		CodecPriority.Reset(settings.CodecPriority);
		FormatPriority.Reset(settings.FormatPriority);

		this.WhenChanged(static viewModel => viewModel.SplitMode)
			.Subscribe
			(mode =>
				{
					IsSplitByDuration = mode is RecordingSplitMode.Duration;
					IsSplitBySize = mode is RecordingSplitMode.Size;
				}
			)
			.DisposeWith(Disposables);

		PersistSetting(settingsAppService, this.WhenChanged(static viewModel => viewModel.HostSelection), static (current, value) => current with { StreamHostSelection = value });
		PersistSetting(settingsAppService, this.WhenChanged(static viewModel => viewModel.ConvertToMp4), static (current, value) => current with { ConvertToMp4 = value });
		PersistSetting(settingsAppService, this.WhenChanged(static viewModel => viewModel.DeleteAfterConvert), static (current, value) => current with { DeleteAfterConvert = value });
		PersistSetting(settingsAppService, this.WhenChanged(static viewModel => viewModel.SplitMode), static (current, value) => current with { SplitMode = value });
		PersistSetting(settingsAppService, this.WhenChanged(static viewModel => viewModel.SplitDurationMinutes), static (current, value) => current with { SplitDuration = TimeSpan.FromMinutes((double)value) }, NumberInputQuietPeriod);
		PersistSetting(settingsAppService, this.WhenChanged(static viewModel => viewModel.SplitSizeMebibytes), static (current, value) => current with { SplitSizeMebibytes = (long)value }, NumberInputQuietPeriod);
		PersistSetting(settingsAppService, this.WhenChanged(static viewModel => viewModel.SplitOnTitleChange), static (current, value) => current with { SplitOnTitleChange = value });
		PersistSetting(settingsAppService, this.WhenChanged(static viewModel => viewModel.IncludeTitleInFileName), static (current, value) => current with { IncludeTitleInFileName = value });
		// 保存可能在线程池上执行，用发出的快照，不去读界面正在改的列表
		PersistSetting(settingsAppService, CodecPriority.WhenChanged(static list => list.Values), static (current, values) => current with { CodecPriority = [.. values.OfType<StreamCodec>()] });
		PersistSetting(settingsAppService, FormatPriority.WhenChanged(static list => list.Values), static (current, values) => current with { FormatPriority = [.. values.OfType<StreamFormat>()] });
	}

	public override void Dispose()
	{
		CodecPriority.Dispose();
		FormatPriority.Dispose();
		base.Dispose();
	}
}
