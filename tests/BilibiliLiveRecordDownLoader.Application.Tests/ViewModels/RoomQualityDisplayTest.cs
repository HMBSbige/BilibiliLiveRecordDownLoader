using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using BilibiliLiveRecordDownLoader.Domain.Shared.Localization;
using BilibiliLiveRecordDownLoader.ViewModels.Dialogs;
using BilibiliLiveRecordDownLoader.ViewModels.Rooms;
using ReactiveUI.Primitives.Signals;
using System.Globalization;

namespace BilibiliLiveRecordDownLoader.Application.Tests.ViewModels;

public sealed class RoomQualityDisplayTest
{
	private static RoomSnapshot Recording(long roomId, QualityRestriction? restriction, StreamCodec codec = StreamCodec.Avc, long quality = 250, IReadOnlyDictionary<long, string>? qualityNames = null)
	{
		return new RoomSnapshot(new RoomSettingsDto { RoomId = roomId }, 0, $"主播{roomId}", null, LiveStatus.Live, RecordStatus.Recording, 0, new ActiveRecordingDto(StreamFormat.Fmp4, codec, quality, restriction, qualityNames ?? new Dictionary<long, string>(), DateTimeOffset.UtcNow), null);
	}

	private static RoomSnapshot Idle(long roomId)
	{
		return new RoomSnapshot(new RoomSettingsDto { RoomId = roomId }, 0, $"主播{roomId}", null, LiveStatus.Live, RecordStatus.Idle, 0, null, null);
	}

	private static RoomItemViewModel Row(RoomSnapshot snapshot)
	{
		return new RoomItemViewModel(snapshot, false, new FakeRoomAppService(), TimeProvider.System);
	}

	private static RoomListViewModel CreateList(FakeRoomAppService rooms, IObservable<string?> cookie)
	{
		PlatformStub platform = new();
		RoomListViewModel viewModel = new(new DialogHostViewModel(), rooms, new SettingsAppService(new FakeConfigManager()), new FakeDanmakuAppService(), platform, platform, new RecordingNotifications(), TimeProvider.System);
		viewModel.ApplySettings(AppConfig.CreateDefault().ToSettingsDto(), cookie);
		viewModel.Sync();
		return viewModel;
	}

	public static IEnumerable<Func<(QualityRestriction Restriction, string Expected)>> RestrictionCases()
	{
		yield return static () => (new QualityRestriction.NotLoggedIn(10000), AppLanguage.Current.QualityRestrictedNotLoggedIn.ToString(AppLanguage.Current.QualitySuperHigh, AppLanguage.Current.QualityOriginal));
		yield return static () => (new QualityRestriction.LoginMayHaveExpired(10000), AppLanguage.Current.QualityRestrictedLoginMayHaveExpired.ToString(AppLanguage.Current.QualityOriginal, AppLanguage.Current.QualitySuperHigh));
		yield return static () => (new QualityRestriction.OtherCodecUnavailable(10000, StreamCodec.Av1, 10000), AppLanguage.Current.QualityRestrictedByCodecUnavailable.ToString("AV1", AppLanguage.Current.QualityOriginal));
		yield return static () => (new QualityRestriction.OtherCodec(10000, StreamCodec.Av1, 400), AppLanguage.Current.QualityRestrictedByCodec.ToString("AV1", AppLanguage.Current.QualityBluRay));
	}

	[Test]
	[MethodDataSource(nameof(RestrictionCases))]
	public async Task RestrictedQualityIsMarkedWithItsReasonAsync(QualityRestriction restriction, string expected)
	{
		using RoomItemViewModel room = Row(Recording(1, restriction));

		using (Assert.Multiple())
		{
			await Assert.That(room.StreamInfo).IsEqualTo("fMP4 · AVC (H.264)");
			await Assert.That(room.QualityText).IsEqualTo(AppLanguage.Current.QualitySuperHigh);
			await Assert.That(room.IsQualityRestricted).IsTrue();
			await Assert.That(room.QualityRestrictionTip).IsEqualTo(expected);
			await Assert.That(room.HelpText).IsEqualTo(expected);
		}
	}

	[Test]
	public async Task RowHelpFollowsTheFailureUntilTheRoomRecordsAsync()
	{
		RoomSnapshot connecting = new(new RoomSettingsDto { RoomId = 1 }, 0, "主播1", null, LiveStatus.Live, RecordStatus.Connecting, 0, null, "连接超时");
		using RoomItemViewModel room = Row(connecting);
		string? failing = room.HelpText;

		room.Apply(Recording(1, new QualityRestriction.NotLoggedIn(10000)), false);

		using (Assert.Multiple())
		{
			await Assert.That(failing).IsEqualTo("连接超时");
			await Assert.That(room.HelpText).IsEqualTo(room.QualityRestrictionTip).And.IsNotNull();
		}
	}

	[Test]
	public async Task RestrictionIsClearedOnceTheRoomRecordsTheOriginalTrueColorAsync()
	{
		using RoomItemViewModel room = Row(Recording(1, new QualityRestriction.NotLoggedIn(10000)));

		room.Apply(Recording(1, null, StreamCodec.Hevc, 25000), false);

		using (Assert.Multiple())
		{
			await Assert.That(room.StreamInfo).IsEqualTo("fMP4 · HEVC (H.265)");
			await Assert.That(room.QualityText).IsEqualTo(AppLanguage.Current.QualityOriginalTrueColor);
			await Assert.That(room.IsQualityRestricted).IsFalse();
			await Assert.That(room.QualityRestrictionTip).IsNull();
			await Assert.That(room.HelpText).IsNull();
		}
	}

	public static IEnumerable<Func<(string Culture, long Quality, string Expected)>> NameCases()
	{
		yield return static () => ("zh-CN", 25000, "2K 原画");
		yield return static () => ("en", 25000, AppLanguage.Current.QualityOriginalTrueColor);
		yield return static () => ("en", 12345, "新画质");
		yield return static () => ("zh-CN", 54321, "54321");
	}

	// 接口给的名称是中文：中文界面先用它，其他语言先用翻译过的已知名称
	[Test]
	[MethodDataSource(nameof(NameCases))]
	public async Task QualityIsNamedAsTheApiNamesItForTheRoomAsync(string culture, long quality, string expected)
	{
		Dictionary<long, string> names = new() { [25000] = "2K 原画", [12345] = "新画质", [10000] = "1080P 高码率" };
		CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
		using RoomItemViewModel restricted = Row(Recording(1, new QualityRestriction.NotLoggedIn(quality), quality: 10000, qualityNames: names));
		using RoomItemViewModel recording = Row(Recording(2, null, quality: quality, qualityNames: names));
		string original = culture is "en" ? AppLanguage.Current.QualityOriginal : "1080P 高码率";

		using (Assert.Multiple())
		{
			await Assert.That(recording.QualityText).IsEqualTo(expected);
			await Assert.That(restricted.QualityRestrictionTip).IsEqualTo(AppLanguage.Current.QualityRestrictedNotLoggedIn.ToString(original, expected));
		}
	}

	[Test]
	public async Task NotLoggedInAfterConfiguringACookieAsksForAReconnectAsync()
	{
		using BehaviorSignal<string?> cookie = new(null);
		FakeRoomAppService rooms = new(Recording(1, new QualityRestriction.NotLoggedIn(10000)));
		using RoomListViewModel viewModel = CreateList(rooms, cookie);
		string? before = viewModel.Rooms.Single().QualityRestrictionTip;

		cookie.OnNext("SESSDATA=x");
		RoomItemViewModel room = viewModel.Rooms.Single();

		using (Assert.Multiple())
		{
			await Assert.That(before).IsEqualTo(AppLanguage.Current.QualityRestrictedNotLoggedIn.ToString(AppLanguage.Current.QualitySuperHigh, AppLanguage.Current.QualityOriginal));
			await Assert.That(room.QualityRestrictionTip).IsEqualTo(AppLanguage.Current.QualityRestrictedLoggedIn.ToString(AppLanguage.Current.QualityOriginal));
			await Assert.That(room.IsQualityRestricted).IsTrue();
		}
	}

	public static IEnumerable<TestDataRow<Func<(RoomSnapshot[] Rooms, string? Cookie, string? Expected)>>> BannerCases()
	{
		QualityRestriction notLoggedIn = new QualityRestriction.NotLoggedIn(10000);
		QualityRestriction otherCodec = new QualityRestriction.OtherCodec(10000, StreamCodec.Av1, 10000);
		QualityRestriction expired = new QualityRestriction.LoginMayHaveExpired(10000);
		RoomSnapshot danmakuOnly = Idle(2) with { Danmaku = new RoomDanmakuSnapshot(true) };

		yield return new(() => ([Recording(1, notLoggedIn), Recording(2, notLoggedIn), Recording(3, otherCodec), Recording(4, expired), Recording(5, null), Idle(6), Recording(7, new QualityRestriction.OtherCodecUnavailable(10000, StreamCodec.Av1, 10000))], null, AppLanguage.Current.QualityRestrictedBanner.ToString(2.ToString(CultureInfo.CurrentCulture))), DisplayName: "only rooms restricted for not being logged in are counted");
		yield return new(() => ([Recording(1, notLoggedIn), danmakuOnly], null, AppLanguage.Current.QualityRestrictedAndDanmakuMaskedBanner.ToString(1.ToString(CultureInfo.CurrentCulture))), DisplayName: "restricted quality and masked danmaku names");
		yield return new(() => ([danmakuOnly, Idle(3)], null, AppLanguage.Current.DanmakuMaskedBanner), DisplayName: "masked danmaku names only");
		yield return new(() => ([Recording(1, otherCodec), Recording(2, expired), Idle(3)], null, null), DisplayName: "nothing that logging in would fix");
		yield return new(() => ([Recording(1, notLoggedIn), danmakuOnly], "SESSDATA=x", null), DisplayName: "already logged in");
	}

	[Test]
	[MethodDataSource(nameof(BannerCases))]
	public async Task BannerOnlyShowsWhatLoggingInWouldFixAsync(RoomSnapshot[] rooms, string? cookie, string? expected)
	{
		using RoomListViewModel viewModel = CreateList(new FakeRoomAppService(rooms), Signal.Return(cookie));

		await Assert.That(viewModel.LoginBannerText).IsEqualTo(expected);
	}

	private sealed class FakeRoomAppService(params RoomSnapshot[] rooms) : RoomAppServiceStub
	{
		public override IReadOnlyList<RoomSnapshot> GetRooms()
		{
			return rooms;
		}
	}
}
