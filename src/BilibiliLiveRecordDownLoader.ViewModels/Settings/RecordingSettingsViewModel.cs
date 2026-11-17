namespace BilibiliLiveRecordDownLoader.ViewModels.Settings;

public sealed partial class RecordingSettingsViewModel : ViewModelBase
{
	private bool _isInitialized;

	public IReadOnlyList<ChoiceOption> RecorderTypeOptions { get; } = ChoiceOption.OfAll<RecorderType>();

	public IReadOnlyList<ChoiceOption> HostSelectionOptions { get; } = ChoiceOption.OfAll<StreamHostSelection>();

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
		CodecPriority.Reset(config.CodecPriority);
		FormatPriority.Reset(config.FormatPriority);

		PersistToConfig(this.WhenChanged(static viewModel => viewModel.RecorderType), static (appConfig, value) => appConfig.RecorderType = value.As<RecorderType>() ?? appConfig.RecorderType);
		PersistToConfig(this.WhenChanged(static viewModel => viewModel.HostSelection), static (appConfig, value) => appConfig.StreamHostSelection = value.As<StreamHostSelection>() ?? appConfig.StreamHostSelection);
		PersistToConfig(this.WhenChanged(static viewModel => viewModel.ConvertToMp4), static (appConfig, value) => appConfig.ConvertToMp4 = value);
		PersistToConfig(this.WhenChanged(static viewModel => viewModel.DeleteAfterConvert), static (appConfig, value) => appConfig.DeleteAfterConvert = value);
		PersistToConfig(CodecPriority.WhenChanged(static list => list.Values), (appConfig, _) => appConfig.CodecPriority = CodecPriority.ToList<StreamCodec>());
		PersistToConfig(FormatPriority.WhenChanged(static list => list.Values), (appConfig, _) => appConfig.FormatPriority = FormatPriority.ToList<StreamFormat>());

		_isInitialized = true;
	}
}
