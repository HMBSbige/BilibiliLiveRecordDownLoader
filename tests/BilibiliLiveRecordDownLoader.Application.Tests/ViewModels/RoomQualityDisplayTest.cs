using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using BilibiliLiveRecordDownLoader.Domain.Shared.Localization;
using BilibiliLiveRecordDownLoader.ViewModels.Rooms;
using Splat;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;

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

	private static RoomListViewModel CreateList(FakeRoomAppService rooms, AppConfig config, Func<CancellationToken, Task<bool>>? showLogin = null)
	{
		RoomListViewModel viewModel = new(rooms, new FakeConfigManager(config), new FakeDanmakuAppService()) { ShowLogin = showLogin ?? (static _ => Task.FromResult(false)) };
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
		}
	}

	[Test]
	public async Task TrueOriginalFromTheMasterPlaylistShowsWithoutAWarningAsync()
	{
		using RoomItemViewModel room = new(Recording(1, null, StreamCodec.Hevc, 25000), false);

		using (Assert.Multiple())
		{
			await Assert.That(room.StreamInfo).IsEqualTo("fMP4 · HEVC (H.265)");
			await Assert.That(room.QualityText).IsEqualTo(AppLanguage.Current.QualityTrueOriginal);
			await Assert.That(room.IsQualityRestricted).IsFalse();
			await Assert.That(room.QualityRestrictionTip).IsNull();
		}
	}

	[Test]
	public async Task RestrictionIsClearedOnceTheRoomRecordsTheOriginalAsync()
	{
		using RoomItemViewModel room = new(Recording(1, new QualityRestriction.NotLoggedIn(10000)), false);

		room.Apply(Recording(1, null, StreamCodec.Hevc, 10000), false);

		using (Assert.Multiple())
		{
			await Assert.That(room.IsQualityRestricted).IsFalse();
			await Assert.That(room.QualityRestrictionTip).IsNull();
			await Assert.That(room.StreamInfo).IsEqualTo("fMP4 · HEVC (H.265)");
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

	// 编码原因和登录可能失效不是登录能解决的，不计入横幅
	[Test]
	public async Task BannerCountsOnlyRoomsRestrictedForNotBeingLoggedInAsync()
	{
		FakeRoomAppService rooms = new
		(
			Recording(1, new QualityRestriction.NotLoggedIn(10000)),
			Recording(2, new QualityRestriction.NotLoggedIn(10000)),
			Recording(3, new QualityRestriction.OtherCodec(10000, StreamCodec.Av1, 10000)),
			Recording(4, new QualityRestriction.LoginMayHaveExpired(10000)),
			Recording(5, null),
			Idle(6),
			Recording(7, new QualityRestriction.OtherCodecUnavailable(10000, StreamCodec.Av1, 10000))
		);
		using RoomListViewModel viewModel = CreateList(rooms, AppConfig.CreateDefault());

		await Assert.That(viewModel.LoginBannerText).IsEqualTo(AppLanguage.Current.QualityRestrictedBanner.ToString(2.ToString(CultureInfo.CurrentCulture)));
	}

	// 仅录弹幕时视频不在录，也要提示昵称会打码
	[Test]
	public async Task BannerMentionsMaskedNamesWhileRecordingDanmakuAsync()
	{
		RoomSnapshot danmakuOnly = Idle(2) with { Danmaku = new RoomDanmakuSnapshot(true, 0, true) };
		using RoomListViewModel withRestriction = CreateList(new FakeRoomAppService(Recording(1, new QualityRestriction.NotLoggedIn(10000)), danmakuOnly), AppConfig.CreateDefault());
		using RoomListViewModel withoutRestriction = CreateList(new FakeRoomAppService(danmakuOnly, Idle(3)), AppConfig.CreateDefault());
		AppConfig loggedIn = AppConfig.CreateDefault();
		loggedIn.Cookie = "SESSDATA=x";
		using RoomListViewModel withCookie = CreateList(new FakeRoomAppService(danmakuOnly), loggedIn);

		using (Assert.Multiple())
		{
			await Assert.That(withRestriction.LoginBannerText).IsEqualTo(AppLanguage.Current.QualityRestrictedAndDanmakuMaskedBanner.ToString(1.ToString(CultureInfo.CurrentCulture)));
			await Assert.That(withoutRestriction.LoginBannerText).IsEqualTo(AppLanguage.Current.DanmakuMaskedBanner);
			await Assert.That(withCookie.LoginBannerText).IsNull();
		}
	}

	[Test]
	public async Task BannerIsHiddenWithoutRoomsRestrictedForNotBeingLoggedInAsync()
	{
		FakeRoomAppService rooms = new(Recording(1, new QualityRestriction.OtherCodec(10000, StreamCodec.Av1, 10000)), Recording(2, new QualityRestriction.LoginMayHaveExpired(10000)), Idle(3));
		using RoomListViewModel viewModel = CreateList(rooms, AppConfig.CreateDefault());

		await Assert.That(viewModel.LoginBannerText).IsNull();
	}

	// 已经在录的直播间要等重新请求候选流才会换画质，配了 Cookie 后横幅立即收起
	[Test]
	public async Task BannerIsHiddenOnceACookieIsConfiguredAsync()
	{
		AppConfig config = AppConfig.CreateDefault();
		FakeRoomAppService rooms = new(Recording(1, new QualityRestriction.NotLoggedIn(10000)));
		using RoomListViewModel viewModel = CreateList(rooms, config);
		string? before = viewModel.LoginBannerText;

		config.Cookie = "SESSDATA=x";
		viewModel.Sync();

		using (Assert.Multiple())
		{
			await Assert.That(before).IsNotNull();
			await Assert.That(viewModel.LoginBannerText).IsNull();
		}
	}

	[Test]
	[Timeout(30_000)]
	[Arguments(true)]
	[Arguments(false)]
	public async Task LoginFromTheBannerHidesItOnlyAfterLoggingInAsync(bool succeeds, CancellationToken cancellationToken)
	{
		AppConfig config = AppConfig.CreateDefault();
		FakeRoomAppService rooms = new(Recording(1, new QualityRestriction.NotLoggedIn(10000)));
		TaskCompletionSource shown = new(TaskCreationOptions.RunContinuationsAsynchronously);
		using RoomListViewModel viewModel = CreateList(rooms, config, _ =>
		{
			if (succeeds)
			{
				config.Cookie = "SESSDATA=x";
			}

			shown.SetResult();
			return Task.FromResult(succeeds);
		});

		((ICommand)viewModel.LoginCommand).Execute(null);
		await shown.Task.WaitAsync(cancellationToken);

		while (succeeds && viewModel.LoginBannerText is not null)
		{
			await Task.Delay(10, cancellationToken);
		}

		await Assert.That(viewModel.LoginBannerText is null).IsEqualTo(succeeds);
	}

	private sealed class FakeRoomAppService(params RoomSnapshot[] rooms) : IRoomAppService
	{
		public long Version => 0;

		public bool IsRecording => rooms.Any(static room => room.RecordStatus is not RecordStatus.Idle);

		public IReadOnlyList<RoomSnapshot> GetRooms()
		{
			return rooms;
		}

		public Task MoveAsync(IReadOnlyCollection<long> roomIds, long? before, CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task<RoomSnapshot> AddAsync(RoomSettingsDto settings, CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task<IReadOnlyList<RoomAddResult>> AddRangeAsync(IReadOnlyList<RoomSettingsDto> settings, CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public async IAsyncEnumerable<FollowedRoomDto> GetFollowedRoomsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			await Task.CompletedTask;
			yield break;
		}

		public Task UpdateAsync(RoomSettingsDto settings, CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task RemoveAsync(IReadOnlyCollection<long> roomIds, CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task RefreshAsync(IReadOnlyCollection<long> roomIds, CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}
	}
}
