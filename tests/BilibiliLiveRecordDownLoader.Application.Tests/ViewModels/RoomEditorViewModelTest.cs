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
	public async Task QualityOptionsGoFromBestToTheBilibiliDefaultThenCustomAsync()
	{
		using RoomEditorViewModel editor = Open(null, out _);

		using (Assert.Multiple())
		{
			await Assert.That(editor.QualityOptions.Select(static option => option.Quality?.Qn ?? -1)).IsEquivalentTo([30000L, 25000, 20000, 15000, 10000, 401, 400, 250, 150, 80, 0, -1], CollectionOrdering.Matching);
			await Assert.That(editor.QualityOptions[1].ToString()).IsEqualTo(AppLanguage.Current.QualityOriginalTrueColor);
			await Assert.That(editor.QualityOptions[^2].ToString()).IsEqualTo(AppLanguage.Current.QualityDefault);
			await Assert.That(editor.QualityOptions[^1].ToString()).IsEqualTo(AppLanguage.Current.QualityCustom);
			await Assert.That((editor.SelectedQuality, editor.IsCustomQuality)).IsEqualTo((new QualityOption(StreamQuality.OriginalTrueColor), false));
		}
	}

	// 列出的 qn 再打开时显示为它的选项；其他 qn，包括手改配置或以后新增的画质，显示为自定义并原样保留
	[Test]
	[Arguments("12345", 12345L, true)]
	[Arguments(" 007 ", 7L, true)]
	[Arguments("\u30007\u3000", 7L, true)]
	[Arguments("401", 401L, false)]
	[Arguments("25000", 25000L, false)]
	[Arguments("0", 0L, false)]
	public async Task CustomQnIsSavedAndReopenedAsync(string text, long qn, bool reopensAsCustom, CancellationToken cancellationToken)
	{
		using RoomEditorViewModel editor = Open(new RoomSettingsDto { RoomId = 1, Quality = StreamQuality.Smooth }, out Task<RoomSettingsDto> submitted);

		editor.SelectedQuality = QualityOption.Custom;
		editor.CustomQnText = text;
		string? error = editor.CustomQnError;
		RoomSettingsDto saved = await SaveAsync(editor, submitted, cancellationToken);
		using RoomEditorViewModel reopened = Open(saved, out Task<RoomSettingsDto> resubmitted);
		(QualityOption selected, bool isCustom, string reopenedText) = (reopened.SelectedQuality, reopened.IsCustomQuality, reopened.CustomQnText);
		RoomSettingsDto resaved = await SaveAsync(reopened, resubmitted, cancellationToken);
		StreamQuality expected = new(qn);

		using (Assert.Multiple())
		{
			await Assert.That(error).IsNull();
			await Assert.That((saved.Quality, resaved.Quality)).IsEqualTo((expected, expected));
			await Assert.That((selected, isCustom, reopenedText)).IsEqualTo(reopensAsCustom ? (QualityOption.Custom, true, qn.ToString(CultureInfo.InvariantCulture)) : (new QualityOption(expected), false, string.Empty));
		}
	}

	// 空着时和房间号一样只是不能保存，不提示错误
	[Test]
	[Arguments("-1", true)]
	[Arguments("+1", true)]
	[Arguments("1.5", true)]
	[Arguments("1e4", true)]
	[Arguments("abc", true)]
	[Arguments("99999999999999999999", true)]
	[Arguments("", false)]
	[Arguments(" ", false)]
	public async Task OnlyANonNegativeIntegerCanBeSavedAsTheCustomQnAsync(string text, bool isError)
	{
		using RoomEditorViewModel editor = Open(new RoomSettingsDto { RoomId = 1 }, out _);

		editor.SelectedQuality = QualityOption.Custom;
		editor.CustomQnText = text;

		using (Assert.Multiple())
		{
			await Assert.That(editor.CustomQnError).IsEqualTo(isError ? AppLanguage.Current.InvalidQn : null);
			await Assert.That(((ICommand)editor.SaveCommand).CanExecute(null)).IsFalse();
		}
	}

	// 没改过的 qn 跟着刚选的画质变，改过的保留
	[Test]
	[Arguments(null, "10000")]
	[Arguments("12345", "12345")]
	public async Task SwitchingToCustomStartsFromTheChosenQnUnlessItWasEditedAsync(string? typed, string expected)
	{
		using RoomEditorViewModel editor = Open(new RoomSettingsDto { RoomId = 1, Quality = StreamQuality.BluRay }, out _);

		editor.SelectedQuality = QualityOption.Custom;
		(bool isCustom, string prefilled) = (editor.IsCustomQuality, editor.CustomQnText);
		editor.CustomQnText = typed ?? prefilled;
		editor.SelectedQuality = new QualityOption(StreamQuality.Original);
		editor.SelectedQuality = QualityOption.Custom;

		using (Assert.Multiple())
		{
			await Assert.That((isCustom, prefilled)).IsEqualTo((true, "400"));
			await Assert.That(editor.CustomQnText).IsEqualTo(expected);
		}
	}

	[Test]
	public async Task UnfinishedCustomQnIsIgnoredOnceAnOptionIsChosenAsync(CancellationToken cancellationToken)
	{
		using RoomEditorViewModel editor = Open(new RoomSettingsDto { RoomId = 1 }, out Task<RoomSettingsDto> submitted);

		editor.SelectedQuality = QualityOption.Custom;
		editor.CustomQnText = "abc";
		editor.SelectedQuality = new QualityOption(StreamQuality.Original);
		(bool isCustom, string? error) = (editor.IsCustomQuality, editor.CustomQnError);
		RoomSettingsDto settings = await SaveAsync(editor, submitted, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That((isCustom, error)).IsEqualTo((false, null));
			await Assert.That(settings.Quality).IsEqualTo(StreamQuality.Original);
		}
	}

	private static RoomEditorViewModel Open(RoomSettingsDto? existing, out Task<RoomSettingsDto> submitted, AppSettingsDto? global = null)
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
