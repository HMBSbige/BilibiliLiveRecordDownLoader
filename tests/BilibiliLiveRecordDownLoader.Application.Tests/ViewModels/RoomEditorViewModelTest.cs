using BilibiliLiveRecordDownLoader.Domain.Shared.Localization;
using BilibiliLiveRecordDownLoader.ViewModels.Common;
using BilibiliLiveRecordDownLoader.ViewModels.Dialogs;
using System.Globalization;
using System.Windows.Input;

namespace BilibiliLiveRecordDownLoader.Application.Tests.ViewModels;

public sealed class RoomEditorViewModelTest
{
	private static readonly AppConfig Global = new() { SplitMode = RecordingSplitMode.Size, SplitDuration = TimeSpan.FromMinutes(45), SplitSizeMegabytes = 2000, SplitOnTitleChange = true };

	[Test]
	public async Task RoomFollowingTheGlobalSplitStartsItsValuesFromTheGlobalOnesAsync()
	{
		using RoomEditorViewModel editor = new(new RoomSettingsDto { RoomId = 1 }, Global, static (_, _) => Task.CompletedTask);

		using (Assert.Multiple())
		{
			await Assert.That(editor.SplitMode).IsEqualTo(ChoiceOption.Inherit);
			await Assert.That(editor.SplitOnTitleChange).IsEqualTo(ChoiceOption.Inherit);
			await Assert.That((editor.IsSplitByDuration, editor.IsSplitBySize)).IsEqualTo((false, false));
			await Assert.That((editor.SplitDurationMinutes, editor.SplitSizeMegabytes)).IsEqualTo((45m, 2000m));
		}
	}

	[Test]
	[Timeout(10_000)]
	public async Task OnlyTheValueOfTheChosenModeIsSavedAsync(CancellationToken cancellationToken)
	{
		TaskCompletionSource<RoomSettingsDto> saved = new(TaskCreationOptions.RunContinuationsAsynchronously);
		using RoomEditorViewModel editor = Open(new RoomSettingsDto { RoomId = 1 }, saved);

		editor.SplitMode = new ChoiceOption(RecordingSplitMode.Duration);
		bool isByDuration = editor.IsSplitByDuration;
		editor.SplitDurationMinutes = 30;
		editor.SplitSizeMegabytes = 100;
		editor.SplitOnTitleChange = new ChoiceOption(false);
		((ICommand)editor.SaveCommand).Execute(null);
		RoomSettingsDto settings = await saved.Task.WaitAsync(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(isByDuration).IsTrue();
			await Assert.That(settings.SplitMode).IsEqualTo(RecordingSplitMode.Duration);
			await Assert.That(settings.SplitDuration).IsEqualTo(TimeSpan.FromMinutes(30));
			await Assert.That(settings.SplitSizeMegabytes).IsNull();
			await Assert.That(settings.SplitOnTitleChange).IsFalse();
		}
	}

	[Test]
	[Timeout(10_000)]
	public async Task OverriddenSplitIsShownAndClearedWhenFollowingTheGlobalAgainAsync(CancellationToken cancellationToken)
	{
		TaskCompletionSource<RoomSettingsDto> saved = new(TaskCreationOptions.RunContinuationsAsynchronously);
		using RoomEditorViewModel editor = Open(new RoomSettingsDto { RoomId = 1, SplitMode = RecordingSplitMode.Size, SplitSizeMegabytes = 1000, SplitOnTitleChange = true }, saved);

		(ChoiceOption mode, bool isBySize, decimal megabytes, ChoiceOption onTitle) = (editor.SplitMode, editor.IsSplitBySize, editor.SplitSizeMegabytes, editor.SplitOnTitleChange);
		editor.SplitMode = ChoiceOption.Inherit;
		editor.SplitOnTitleChange = ChoiceOption.Inherit;
		((ICommand)editor.SaveCommand).Execute(null);
		RoomSettingsDto settings = await saved.Task.WaitAsync(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(mode).IsEqualTo(new ChoiceOption(RecordingSplitMode.Size));
			await Assert.That(isBySize).IsTrue();
			await Assert.That(megabytes).IsEqualTo(1000m);
			await Assert.That(onTitle).IsEqualTo(new ChoiceOption(true));
			await Assert.That((settings.SplitMode, settings.SplitDuration, settings.SplitSizeMegabytes, settings.SplitOnTitleChange)).IsEqualTo((null, null, null, null));
		}
	}

	[Test]
	[Timeout(10_000)]
	public async Task EmptyDanmakuOffsetFollowsTheGlobalOneAsync(CancellationToken cancellationToken)
	{
		TaskCompletionSource<RoomSettingsDto> saved = new(TaskCreationOptions.RunContinuationsAsynchronously);
		AppConfig global = new() { DanmakuOffset = TimeSpan.FromSeconds(0.5) };
		using RoomEditorViewModel editor = new(new RoomSettingsDto { RoomId = 1 }, global, (settings, _) =>
		{
			saved.TrySetResult(settings);
			return Task.CompletedTask;
		});

		(decimal? offset, string placeholder) = (editor.DanmakuOffsetSeconds, editor.DanmakuOffsetPlaceholder);
		((ICommand)editor.SaveCommand).Execute(null);
		RoomSettingsDto settings = await saved.Task.WaitAsync(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(offset).IsNull();
			await Assert.That(placeholder).IsEqualTo(AppLanguage.Current.InheritValue.ToString(0.5m.ToString("0.###", CultureInfo.CurrentCulture)));
			await Assert.That(settings.DanmakuOffset).IsNull();
		}
	}

	[Test]
	[Timeout(10_000)]
	public async Task DanmakuSettingsOfTheRoomAreSavedAsync(CancellationToken cancellationToken)
	{
		TaskCompletionSource<RoomSettingsDto> saved = new(TaskCreationOptions.RunContinuationsAsynchronously);
		using RoomEditorViewModel editor = Open(new RoomSettingsDto { RoomId = 1, DanmakuOffset = TimeSpan.FromSeconds(-1) }, saved);

		decimal? restored = editor.DanmakuOffsetSeconds;
		editor.IsDanmakuEnabled = true;
		editor.DanmakuOffsetSeconds = 1.25m;
		((ICommand)editor.SaveCommand).Execute(null);
		RoomSettingsDto settings = await saved.Task.WaitAsync(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(restored).IsEqualTo(-1m);
			await Assert.That(settings.IsDanmakuEnabled).IsTrue();
			await Assert.That(settings.DanmakuOffset).IsEqualTo(TimeSpan.FromSeconds(1.25));
		}
	}

	private static RoomEditorViewModel Open(RoomSettingsDto existing, TaskCompletionSource<RoomSettingsDto> saved)
	{
		return new RoomEditorViewModel(existing, Global, (settings, _) =>
		{
			saved.TrySetResult(settings);
			return Task.CompletedTask;
		});
	}
}
