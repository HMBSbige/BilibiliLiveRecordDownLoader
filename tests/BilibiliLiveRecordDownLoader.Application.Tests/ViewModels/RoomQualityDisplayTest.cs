using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using BilibiliLiveRecordDownLoader.Domain.Shared.Localization;
using BilibiliLiveRecordDownLoader.ViewModels.Rooms;
using Splat;
using System.Globalization;

namespace BilibiliLiveRecordDownLoader.Application.Tests.ViewModels;

public sealed class RoomQualityDisplayTest
{
	// 列表项读取 TimeProvider 时走全局定位器
	[Before(Class)]
	public static void RegisterServices()
	{
		AppLocator.CurrentMutable.RegisterConstant(TimeProvider.System);
	}

	private static RoomSnapshot Recording(long roomId, QualityRestriction? restriction, StreamCodec codec = StreamCodec.Avc, long quality = 250)
	{
		return new RoomSnapshot(new RoomSettingsDto { RoomId = roomId }, 0, $"主播{roomId}", null, LiveStatus.Live, RecordStatus.Recording, 0, new ActiveRecordingDto(StreamFormat.Fmp4, codec, quality, restriction, DateTimeOffset.UtcNow), null);
	}

	private static RoomSnapshot Idle(long roomId)
	{
		return new RoomSnapshot(new RoomSettingsDto { RoomId = roomId }, 0, $"主播{roomId}", null, LiveStatus.Live, RecordStatus.Idle, 0, null, null);
	}

	private static RoomListViewModel CreateList(FakeRoomAppService rooms, AppConfig config)
	{
		RoomListViewModel viewModel = new(rooms, new FakeConfigManager(config), new FakeDanmakuAppService());
		viewModel.ApplyConfig(config);
		viewModel.Sync();
		return viewModel;
	}

	public static IEnumerable<Func<(QualityRestriction Restriction, string Expected)>> RestrictionCases()
	{
		yield return () => (new QualityRestriction.NotLoggedIn(10000), AppLanguage.Current.QualityRestrictedNotLoggedIn.ToString(AppLanguage.Current.QualitySuperHigh, AppLanguage.Current.QualityOriginal));
		yield return () => (new QualityRestriction.LoginMayHaveExpired(10000), AppLanguage.Current.QualityRestrictedLoginMayHaveExpired.ToString(AppLanguage.Current.QualityOriginal, AppLanguage.Current.QualitySuperHigh));
		yield return () => (new QualityRestriction.OtherCodec(10000, StreamCodec.Av1, 10000), AppLanguage.Current.QualityRestrictedByCodec.ToString("AV1", AppLanguage.Current.QualityOriginal));
		yield return () => (new QualityRestriction.OtherCodecUnavailable(10000, StreamCodec.Av1, 10000), AppLanguage.Current.QualityRestrictedByCodecUnavailable.ToString("AV1", AppLanguage.Current.QualityOriginal));
		// 另一种编码只能给到蓝光时，提示的是蓝光而不是目标档位
		yield return () => (new QualityRestriction.OtherCodec(10000, StreamCodec.Av1, 400), AppLanguage.Current.QualityRestrictedByCodec.ToString("AV1", AppLanguage.Current.QualityBluRay));
		// 所选画质低于最高画质时，提示里写的是所选的
		yield return () => (new QualityRestriction.NotLoggedIn(400), AppLanguage.Current.QualityRestrictedNotLoggedIn.ToString(AppLanguage.Current.QualitySuperHigh, AppLanguage.Current.QualityBluRay));
	}

	[Test]
	[MethodDataSource(nameof(RestrictionCases))]
	public async Task RestrictedQualityIsMarkedWithItsReasonAsync(QualityRestriction restriction, string expected)
	{
		using RoomItemViewModel room = new(Recording(1, restriction), false);

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
		using RoomItemViewModel room = new(connecting, false);
		string? failing = room.HelpText;

		room.Apply(Recording(1, new QualityRestriction.NotLoggedIn(10000)), false);

		using (Assert.Multiple())
		{
			await Assert.That(failing).IsEqualTo("连接超时");
			await Assert.That(room.HelpText).IsEqualTo(room.QualityRestrictionTip).And.IsNotNull();
		}
	}

	// 受限时改从主播放列表取到的真原画不再受限
	[Test]
	public async Task RestrictionIsClearedOnceTheRoomRecordsTheTrueOriginalAsync()
	{
		using RoomItemViewModel room = new(Recording(1, new QualityRestriction.NotLoggedIn(10000)), false);

		room.Apply(Recording(1, null, StreamCodec.Hevc, 25000), false);

		using (Assert.Multiple())
		{
			await Assert.That(room.StreamInfo).IsEqualTo("fMP4 · HEVC (H.265)");
			await Assert.That(room.QualityText).IsEqualTo(AppLanguage.Current.QualityTrueOriginal);
			await Assert.That(room.IsQualityRestricted).IsFalse();
			await Assert.That(room.QualityRestrictionTip).IsNull();
			await Assert.That(room.HelpText).IsNull();
		}
	}

	// 稳定不断的流不会重新请求候选流，登录之后受限原因仍是登录前判断的未登录
	[Test]
	public async Task NotLoggedInAfterConfiguringACookieAsksForAReconnectAsync()
	{
		AppConfig config = AppConfig.CreateDefault();
		FakeRoomAppService rooms = new(Recording(1, new QualityRestriction.NotLoggedIn(10000)));
		using RoomListViewModel viewModel = CreateList(rooms, config);
		string? before = viewModel.Rooms.Single().QualityRestrictionTip;

		config.Cookie = "SESSDATA=x";
		viewModel.Sync();
		RoomItemViewModel room = viewModel.Rooms.Single();

		using (Assert.Multiple())
		{
			await Assert.That(before).IsEqualTo(AppLanguage.Current.QualityRestrictedNotLoggedIn.ToString(AppLanguage.Current.QualitySuperHigh, AppLanguage.Current.QualityOriginal));
			await Assert.That(room.QualityRestrictionTip).IsEqualTo(AppLanguage.Current.QualityRestrictedLoggedIn.ToString(AppLanguage.Current.QualityOriginal));
			await Assert.That(room.IsQualityRestricted).IsTrue();
		}
	}

	// 登录可能失效是按请求时配了 Cookie 判断的，和现在有没有配无关
	[Test]
	public async Task OnlyNotLoggedInDependsOnTheCurrentCookieAsync()
	{
		QualityRestriction expired = new QualityRestriction.LoginMayHaveExpired(10000);

		await Assert.That(RoomItemViewModel.DescribeRestriction(expired, 250, false)).IsEqualTo(RoomItemViewModel.DescribeRestriction(expired, 250, true));
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
		AppConfig config = AppConfig.CreateDefault();
		config.Cookie = cookie;
		using RoomListViewModel viewModel = CreateList(new FakeRoomAppService(rooms), config);

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
