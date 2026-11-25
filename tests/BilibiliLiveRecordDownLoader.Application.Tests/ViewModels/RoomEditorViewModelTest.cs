using BilibiliLiveRecordDownLoader.Domain.Shared.Localization;
using BilibiliLiveRecordDownLoader.ViewModels.Common;
using BilibiliLiveRecordDownLoader.ViewModels.Dialogs;
using System.Globalization;
using System.Windows.Input;

namespace BilibiliLiveRecordDownLoader.Application.Tests.ViewModels;

public sealed class RoomEditorViewModelTest
{
	private static readonly AppSettingsDto Global = AppConfig.CreateDefault().ToSettingsDto() with { SplitMode = RecordingSplitMode.Size, SplitDuration = TimeSpan.FromMinutes(45), SplitSizeMebibytes = 2000, SplitOnTitleChange = true };

	[Test]
	public async Task RoomFollowingTheGlobalSplitStartsItsValuesFromTheGlobalOnesAsync()
	{
		using RoomEditorViewModel editor = Open(new RoomSettingsDto { RoomId = 1 }, out _);

		using (Assert.Multiple())
		{
			await Assert.That(editor.SplitMode).IsEqualTo(ChoiceOption.Inherit);
			await Assert.That(editor.SplitOnTitleChange).IsEqualTo(ChoiceOption.Inherit);
			await Assert.That((editor.IsSplitByDuration, editor.IsSplitBySize)).IsEqualTo((false, false));
			await Assert.That((editor.SplitDurationMinutes, editor.SplitSizeMebibytes)).IsEqualTo((45m, 2000m));
		}
	}

	[Test]
	public async Task OnlyTheValueOfTheChosenModeIsSavedAsync(CancellationToken cancellationToken)
	{
		using RoomEditorViewModel editor = Open(new RoomSettingsDto { RoomId = 1 }, out Task<RoomSettingsDto> submitted);

		editor.SplitMode = new ChoiceOption(RecordingSplitMode.Duration);
		bool isByDuration = editor.IsSplitByDuration;
		editor.SplitDurationMinutes = 30;
		editor.SplitSizeMebibytes = 100;
		editor.SplitOnTitleChange = new ChoiceOption(false);
		RoomSettingsDto settings = await SaveAsync(editor, submitted, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(isByDuration).IsTrue();
			await Assert.That((settings.SplitMode, settings.SplitDuration, settings.SplitSizeMebibytes, settings.SplitOnTitleChange)).IsEqualTo((RecordingSplitMode.Duration, TimeSpan.FromMinutes(30), null, false));
		}
	}

	[Test]
	public async Task OverriddenSplitIsShownAndClearedWhenFollowingTheGlobalAgainAsync(CancellationToken cancellationToken)
	{
		using RoomEditorViewModel editor = Open(new RoomSettingsDto { RoomId = 1, SplitMode = RecordingSplitMode.Size, SplitSizeMebibytes = 1000, SplitOnTitleChange = true }, out Task<RoomSettingsDto> submitted);

		(ChoiceOption mode, bool isBySize, decimal mebibytes, ChoiceOption onTitle) = (editor.SplitMode, editor.IsSplitBySize, editor.SplitSizeMebibytes, editor.SplitOnTitleChange);
		editor.SplitMode = ChoiceOption.Inherit;
		editor.SplitOnTitleChange = ChoiceOption.Inherit;
		RoomSettingsDto settings = await SaveAsync(editor, submitted, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That((mode, isBySize, mebibytes, onTitle)).IsEqualTo((new ChoiceOption(RecordingSplitMode.Size), true, 1000m, new ChoiceOption(true)));
			await Assert.That((settings.SplitMode, settings.SplitDuration, settings.SplitSizeMebibytes, settings.SplitOnTitleChange)).IsEqualTo((null, null, null, null));
		}
	}

	[Test]
	public async Task EmptyDanmakuOffsetFollowsTheGlobalOneAsync(CancellationToken cancellationToken)
	{
		using RoomEditorViewModel editor = Open(new RoomSettingsDto { RoomId = 1 }, out Task<RoomSettingsDto> submitted, Global with { DanmakuOffset = TimeSpan.FromSeconds(0.5) });

		(decimal? offset, string placeholder) = (editor.DanmakuOffsetSeconds, editor.DanmakuOffsetPlaceholder);
		RoomSettingsDto settings = await SaveAsync(editor, submitted, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(offset).IsNull();
			await Assert.That(placeholder).IsEqualTo(AppLanguage.Current.InheritValue.ToString(0.5m.ToString("0.###", CultureInfo.CurrentCulture)));
			await Assert.That(settings.DanmakuOffset).IsNull();
		}
	}

	[Test]
	public async Task DanmakuSettingsOfTheRoomAreSavedAsync(CancellationToken cancellationToken)
	{
		using RoomEditorViewModel editor = Open(new RoomSettingsDto { RoomId = 1, DanmakuOffset = TimeSpan.FromSeconds(-1) }, out Task<RoomSettingsDto> submitted);

		decimal? restored = editor.DanmakuOffsetSeconds;
		editor.IsDanmakuEnabled = true;
		editor.DanmakuOffsetSeconds = 1.25m;
		RoomSettingsDto settings = await SaveAsync(editor, submitted, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(restored).IsEqualTo(-1m);
			await Assert.That((settings.IsDanmakuEnabled, settings.DanmakuOffset)).IsEqualTo((true, TimeSpan.FromSeconds(1.25)));
		}
	}

	[Test]
	public async Task QualityOptionsGoFromBestToTheBilibiliDefaultAsync()
	{
		using RoomEditorViewModel editor = Open(new RoomSettingsDto { RoomId = 1 }, out _);

		using (Assert.Multiple())
		{
			await Assert.That(editor.QualityOptions.Select(static option => option.As<StreamQuality>()?.Qn ?? -1)).IsEquivalentTo([30000L, 25000, 20000, 15000, 10000, 400, 250, 150, 80, 0L], CollectionOrdering.Matching);
			await Assert.That(editor.QualityOptions[1].ToString()).IsEqualTo(AppLanguage.Current.QualityOriginalTrueColor);
			await Assert.That(editor.QualityOptions[^1].ToString()).IsEqualTo(AppLanguage.Current.QualityDefault);
			await Assert.That(editor.Quality).IsEqualTo(StreamQuality.Original);
		}
	}

	// 手改配置或以后新增的画质
	[Test]
	[Arguments(12345L, 4, "12345")]
	[Arguments(401L, 5, null)]
	public async Task QualityOutsideTheOptionsIsListedInPlaceAndKeptAsync(long qn, int index, string? label, CancellationToken cancellationToken)
	{
		using RoomEditorViewModel editor = Open(new RoomSettingsDto { RoomId = 1, Quality = new StreamQuality(qn) }, out Task<RoomSettingsDto> submitted);

		IReadOnlyList<ChoiceOption> options = editor.QualityOptions;
		RoomSettingsDto settings = await SaveAsync(editor, submitted, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(options).Count().IsEqualTo(StreamQuality.Selectable.Count + 1);
			await Assert.That(options[index].As<StreamQuality>()).IsEqualTo(new StreamQuality(qn));
			await Assert.That(options[index].ToString()).IsEqualTo(label ?? AppLanguage.Current.QualityBluRayDolby);
			await Assert.That(settings.Quality).IsEqualTo(new StreamQuality(qn));
		}
	}

	private static RoomEditorViewModel Open(RoomSettingsDto existing, out Task<RoomSettingsDto> submitted, AppSettingsDto? global = null)
	{
		TaskCompletionSource<RoomSettingsDto> submission = new(TaskCreationOptions.RunContinuationsAsynchronously);
		submitted = submission.Task;

		return new RoomEditorViewModel(existing, global ?? Global, (settings, _) =>
		{
			submission.TrySetResult(settings);
			return Task.CompletedTask;
		});
	}

	private static async Task<RoomSettingsDto> SaveAsync(RoomEditorViewModel editor, Task<RoomSettingsDto> submitted, CancellationToken cancellationToken)
	{
		((ICommand)editor.SaveCommand).Execute(null);
		return await submitted.WaitAsync(cancellationToken);
	}
}
