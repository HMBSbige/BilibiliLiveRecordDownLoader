namespace BilibiliLiveRecordDownLoader.ViewModels.Dialogs;

public sealed partial class RoomEditorViewModel : DialogViewModel<RoomSettingsDto>
{
	private readonly RoomSettingsDto _original;
	private readonly Func<RoomSettingsDto, CancellationToken, Task> _submit;

	public RoomEditorViewModel(RoomSettingsDto? existing, AppConfig globalConfig, Func<RoomSettingsDto, CancellationToken, Task> submit)
	{
		_submit = submit;
		IsNew = existing is null;
		_original = existing ?? new RoomSettingsDto { RoomId = 0 };

		RoomIdText = IsNew ? string.Empty : _original.RoomId.ToString(CultureInfo.InvariantCulture);
		IsRecordingEnabled = _original.IsRecordingEnabled;
		IsNotificationEnabled = _original.IsNotificationEnabled;
		IsDanmakuEnabled = _original.IsDanmakuEnabled;
		DanmakuOffsetSeconds = _original.DanmakuOffset is { } offset ? (decimal)offset.TotalSeconds : null;
		DanmakuOffsetPlaceholder = AppLanguage.Current.InheritValue.ToString(((decimal)globalConfig.DanmakuOffset.TotalSeconds).ToString("0.###", CultureInfo.CurrentCulture));
		Quality = _original.Quality;
		RecorderType = _original.RecorderType is { } recorderType ? new ChoiceOption(recorderType) : ChoiceOption.Inherit;
		ConvertToMp4 = new ChoiceOption(_original.ConvertToMp4);
		DeleteAfterConvert = new ChoiceOption(_original.DeleteAfterConvert);
		SplitMode = _original.SplitMode is { } splitMode ? new ChoiceOption(splitMode) : ChoiceOption.Inherit;
		SplitOnTitleChange = new ChoiceOption(_original.SplitOnTitleChange);

		SplitDurationMinutes = (decimal)(_original.SplitDuration ?? globalConfig.SplitDuration).TotalMinutes;
		SplitSizeMegabytes = _original.SplitSizeMegabytes ?? globalConfig.SplitSizeMegabytes;
		OverrideCodecPriority = _original.CodecPriority is not null;
		OverrideFormatPriority = _original.FormatPriority is not null;
		CodecPriority = PriorityListViewModel.Create(_original.CodecPriority ?? globalConfig.CodecPriority).DisposeWith(Disposables);
		FormatPriority = PriorityListViewModel.Create(_original.FormatPriority ?? globalConfig.FormatPriority).DisposeWith(Disposables);
		StatusCheckIntervalSeconds = (decimal)_original.StatusCheckInterval.TotalSeconds;
		DanmuReconnectDelaySeconds = (decimal)_original.DanmuReconnectDelay.TotalSeconds;
		StreamReconnectDelaySeconds = (decimal)_original.StreamReconnectDelay.TotalSeconds;
		StreamConnectTimeoutSeconds = (decimal)_original.StreamConnectTimeout.TotalSeconds;
		StreamIdleTimeoutSeconds = (decimal)_original.StreamIdleTimeout.TotalSeconds;

		CanSave = this.WhenChanged(static viewModel => viewModel.RoomIdText).Map(static text => TryParseRoomId(text, out _));
		SaveCommand.DisposeWith(Disposables);

		this.WhenChanged(static viewModel => viewModel.SplitMode)
			.Map(static mode => mode.As<RecordingSplitMode>())
			.Subscribe(mode => (IsSplitByDuration, IsSplitBySize) = (mode is RecordingSplitMode.Duration, mode is RecordingSplitMode.Size))
			.DisposeWith(Disposables);

		this.WhenChanged(static viewModel => viewModel.RoomIdText)
			.Subscribe
			(text =>
				{
					RoomIdError = string.IsNullOrWhiteSpace(text) || TryParseRoomId(text, out _) ? null : AppLanguage.Current.InvalidRoomId;
					ErrorMessage = null;
				}
			)
			.DisposeWith(Disposables);
	}

	public bool IsNew { get; }

	public override string Title => IsNew ? AppLanguage.Current.AddRoom : AppLanguage.Current.EditRoom;

	public IReadOnlyList<ChoiceOption> QualityOptions { get; } = ChoiceOption.Of(StreamQuality.Dolby, StreamQuality.UltraHd4K, StreamQuality.Original, StreamQuality.BluRay, StreamQuality.SuperHigh, StreamQuality.High, StreamQuality.Smooth);

	public IReadOnlyList<ChoiceOption> RecorderTypeOptions { get; } = ChoiceOption.InheritOr<RecorderType>();

	public IReadOnlyList<ChoiceOption> SplitModeOptions { get; } = ChoiceOption.InheritOr<RecordingSplitMode>();

	// 视图通过实例绑定访问这个属性
#pragma warning disable CA1822
	public IReadOnlyList<ChoiceOption> ToggleOptions => ChoiceOption.Toggles;
#pragma warning restore CA1822

	public PriorityListViewModel CodecPriority { get; }

	public PriorityListViewModel FormatPriority { get; }

	public IObservable<bool> CanSave { get; }

	[Reactive]
	public partial string RoomIdText { get; set; }

	[Reactive]
	public partial string? RoomIdError { get; private set; }

	[Reactive]
	public partial string? ErrorMessage { get; private set; }

	[Reactive]
	public partial bool IsRecordingEnabled { get; set; }

	[Reactive]
	public partial bool IsNotificationEnabled { get; set; }

	[Reactive]
	public partial bool IsDanmakuEnabled { get; set; }

	// 留空时用全局的偏移
	[Reactive]
	public partial decimal? DanmakuOffsetSeconds { get; set; }

	public string DanmakuOffsetPlaceholder { get; }

	[Reactive]
	public partial StreamQuality Quality { get; set; }

	[Reactive]
	public partial ChoiceOption RecorderType { get; set; }

	[Reactive]
	public partial ChoiceOption ConvertToMp4 { get; set; }

	[Reactive]
	public partial ChoiceOption DeleteAfterConvert { get; set; }

	[Reactive]
	public partial ChoiceOption SplitMode { get; set; }

	[Reactive]
	public partial decimal SplitDurationMinutes { get; set; }

	[Reactive]
	public partial decimal SplitSizeMegabytes { get; set; }

	[Reactive]
	public partial ChoiceOption SplitOnTitleChange { get; set; }

	[Reactive]
	public partial bool IsSplitByDuration { get; private set; }

	[Reactive]
	public partial bool IsSplitBySize { get; private set; }

	[Reactive]
	public partial bool OverrideCodecPriority { get; set; }

	[Reactive]
	public partial bool OverrideFormatPriority { get; set; }

	[Reactive]
	public partial decimal StatusCheckIntervalSeconds { get; set; }

	[Reactive]
	public partial decimal DanmuReconnectDelaySeconds { get; set; }

	[Reactive]
	public partial decimal StreamReconnectDelaySeconds { get; set; }

	[Reactive]
	public partial decimal StreamConnectTimeoutSeconds { get; set; }

	[Reactive]
	public partial decimal StreamIdleTimeoutSeconds { get; set; }

	[ReactiveCommand(CanExecute = nameof(CanSave))]
	private async Task SaveAsync(CancellationToken cancellationToken)
	{
		if (!TryParseRoomId(RoomIdText, out long roomId))
		{
			return;
		}

		RoomSettingsDto settings = _original with
		{
			RoomId = roomId,
			IsRecordingEnabled = IsRecordingEnabled,
			IsNotificationEnabled = IsNotificationEnabled,
			IsDanmakuEnabled = IsDanmakuEnabled,
			DanmakuOffset = DanmakuOffsetSeconds is { } seconds ? ConfigValues.DanmakuOffsetFromSeconds(seconds) : null,
			Quality = Quality,
			RecorderType = RecorderType.As<RecorderType>(),
			CodecPriority = OverrideCodecPriority ? CodecPriority.ToList<StreamCodec>() : null,
			FormatPriority = OverrideFormatPriority ? FormatPriority.ToList<StreamFormat>() : null,
			ConvertToMp4 = ConvertToMp4.As<bool>(),
			DeleteAfterConvert = DeleteAfterConvert.As<bool>(),
			SplitMode = SplitMode.As<RecordingSplitMode>(),
			SplitDuration = IsSplitByDuration ? TimeSpan.FromMinutes((double)SplitDurationMinutes) : null,
			SplitSizeMegabytes = IsSplitBySize ? (long)SplitSizeMegabytes : null,
			SplitOnTitleChange = SplitOnTitleChange.As<bool>(),
			StatusCheckInterval = TimeSpan.FromSeconds((double)StatusCheckIntervalSeconds),
			DanmuReconnectDelay = TimeSpan.FromSeconds((double)DanmuReconnectDelaySeconds),
			StreamReconnectDelay = TimeSpan.FromSeconds((double)StreamReconnectDelaySeconds),
			StreamConnectTimeout = TimeSpan.FromSeconds((double)StreamConnectTimeoutSeconds),
			StreamIdleTimeout = TimeSpan.FromSeconds((double)StreamIdleTimeoutSeconds)
		};

		ErrorMessage = null;
		IsBusy = true;

		try
		{
			await _submit(settings, cancellationToken);
			Close(settings);
		}
		catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
		{
			ErrorMessage = exception.Message;
		}
		finally
		{
			IsBusy = false;
		}
	}

	private static bool TryParseRoomId(string? text, out long roomId)
	{
		return long.TryParse(text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out roomId) && roomId > 0;
	}
}
