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
		using RoomEditorViewModel editor = Open(new RoomSettingsDto { RoomId = 1 }, out _);

		using (Assert.Multiple())
		{
			await Assert.That(editor.SplitMode).IsEqualTo(ChoiceOption.Inherit);
			await Assert.That(editor.SplitOnTitleChange).IsEqualTo(ChoiceOption.Inherit);
			await Assert.That((editor.IsSplitByDuration, editor.IsSplitBySize)).IsEqualTo((false, false));
			await Assert.That((editor.SplitDurationMinutes, editor.SplitSizeMegabytes)).IsEqualTo((45m, 2000m));
		}
	}

	[Test]
	public async Task OnlyTheValueOfTheChosenModeIsSavedAsync(CancellationToken cancellationToken)
	{
		using RoomEditorViewModel editor = Open(new RoomSettingsDto { RoomId = 1 }, out Task<RoomSettingsDto> submitted);

		editor.SplitMode = new ChoiceOption(RecordingSplitMode.Duration);
		bool isByDuration = editor.IsSplitByDuration;
		editor.SplitDurationMinutes = 30;
		editor.SplitSizeMegabytes = 100;
		editor.SplitOnTitleChange = new ChoiceOption(false);
		RoomSettingsDto settings = await SaveAsync(editor, submitted, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(isByDuration).IsTrue();
			await Assert.That((settings.SplitMode, settings.SplitDuration, settings.SplitSizeMegabytes, settings.SplitOnTitleChange)).IsEqualTo((RecordingSplitMode.Duration, TimeSpan.FromMinutes(30), null, false));
		}
	}

	[Test]
	public async Task OverriddenSplitIsShownAndClearedWhenFollowingTheGlobalAgainAsync(CancellationToken cancellationToken)
	{
		using RoomEditorViewModel editor = Open(new RoomSettingsDto { RoomId = 1, SplitMode = RecordingSplitMode.Size, SplitSizeMegabytes = 1000, SplitOnTitleChange = true }, out Task<RoomSettingsDto> submitted);

		(ChoiceOption mode, bool isBySize, decimal megabytes, ChoiceOption onTitle) = (editor.SplitMode, editor.IsSplitBySize, editor.SplitSizeMegabytes, editor.SplitOnTitleChange);
		editor.SplitMode = ChoiceOption.Inherit;
		editor.SplitOnTitleChange = ChoiceOption.Inherit;
		RoomSettingsDto settings = await SaveAsync(editor, submitted, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That((mode, isBySize, megabytes, onTitle)).IsEqualTo((new ChoiceOption(RecordingSplitMode.Size), true, 1000m, new ChoiceOption(true)));
			await Assert.That((settings.SplitMode, settings.SplitDuration, settings.SplitSizeMegabytes, settings.SplitOnTitleChange)).IsEqualTo((null, null, null, null));
		}
	}

	[Test]
	public async Task EmptyDanmakuOffsetFollowsTheGlobalOneAsync(CancellationToken cancellationToken)
	{
		using RoomEditorViewModel editor = Open(new RoomSettingsDto { RoomId = 1 }, out Task<RoomSettingsDto> submitted, new AppConfig { DanmakuOffset = TimeSpan.FromSeconds(0.5) });

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

	// 检查交给保存的设置，而不是对话框的结果
	private static RoomEditorViewModel Open(RoomSettingsDto existing, out Task<RoomSettingsDto> submitted, AppConfig? global = null)
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
