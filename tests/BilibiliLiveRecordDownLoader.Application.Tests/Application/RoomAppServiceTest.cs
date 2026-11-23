using BilibiliLiveRecordDownLoader.Application.Contracts.Accounts;
using BilibiliLiveRecordDownLoader.Application.Contracts.Platform;
using BilibiliLiveRecordDownLoader.Application.Http;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using BilibiliLiveRecordDownLoader.Domain.Shared.Localization;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using System.Web;
using TUnit.Assertions.Enums;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class RoomAppServiceTest
{
	private const long ExistingRoomId = 22907643;

	private const long FirstRoomId = 6154037;
	private const long SecondRoomId = 5440;
	private const long ThirdRoomId = 21452505;
	private const long FourthRoomId = 7777;
	private const long FifthRoomId = 8888;

	private static readonly (long RoomId, long ShortId, string UserName)[] KnownRooms = [(FirstRoomId, 732, "Asaki大人"), (SecondRoomId, 1, "官方"), (ExistingRoomId, 0, "已有"), (ThirdRoomId, 0, "第三"), (FourthRoomId, 0, "第四"), (FifthRoomId, 0, "第五")];

	[Test]
	[Timeout(30_000)]
	public async Task AddRangeResolvesShortIdsInOneLookupAndWritesConfigOnceAsync(CancellationToken cancellationToken)
	{
		await using Harness harness = await Harness.CreateAsync();

		IReadOnlyList<RoomAddResult> results = await harness.Rooms.AddRangeAsync([Room(732), Room(ExistingRoomId), Room(6154037), Room(1)], cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(results[0]).IsTypeOf<RoomAddResult.Added>()
				.And.Member(static added => added.RequestedRoomId, static id => id.IsEqualTo(732))
				.And.Member(static added => added.Room.RoomId, static id => id.IsEqualTo(6154037))
				.And.Member(static added => added.Room.ShortId, static id => id.IsEqualTo(732))
				.And.Member(static added => added.Room.UserName, static name => name.IsEqualTo("Asaki大人"));
			await Assert.That(results[1]).IsEqualTo(new RoomAddResult.Skipped(ExistingRoomId, ExistingRoomId));
			await Assert.That(results[2]).IsEqualTo(new RoomAddResult.Skipped(6154037, 6154037));
			await Assert.That(results[3]).IsTypeOf<RoomAddResult.Added>()
				.And.Member(static added => added.RequestedRoomId, static id => id.IsEqualTo(1))
				.And.Member(static added => added.Room.RoomId, static id => id.IsEqualTo(5440));
			await Assert.That(harness.Config.UpdateCount).IsEqualTo(1);
			await Assert.That(harness.ConfiguredRoomIds).IsEquivalentTo([ExistingRoomId, 6154037, 5440], CollectionOrdering.Matching);
			await Assert.That(harness.Rooms.GetRooms().Select(static room => room.RoomId)).IsEquivalentTo([ExistingRoomId, 6154037, 5440], CollectionOrdering.Matching);
			await Assert.That(harness.RoomInfoQueries).Contains("732,6154037,1");
			await Assert.That(harness.RoomInfoQueries).DoesNotContain("732");
			await Assert.That(harness.RoomInfoQueries).DoesNotContain("1");
		}
	}

	[Test]
	[Timeout(30_000)]
	public async Task AddRangeReportsFailuresWithoutDroppingTheOtherRoomsAsync(CancellationToken cancellationToken)
	{
		await using Harness harness = await Harness.CreateAsync();

		IReadOnlyList<RoomAddResult> results = await harness.Rooms.AddRangeAsync([Room(404404), Room(0), Room(5440)], cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(results[0]).IsEqualTo(new RoomAddResult.Failed(404404, AppLanguage.Current.RoomNotFound.ToString(404404)));
			await Assert.That(results[1]).IsEqualTo(new RoomAddResult.Failed(0, AppLanguage.Current.InvalidRoomId));
			await Assert.That(results[2]).IsTypeOf<RoomAddResult.Added>().And.Member(static added => added.RequestedRoomId, static id => id.IsEqualTo(5440));
			await Assert.That(harness.Config.UpdateCount).IsEqualTo(1);
			await Assert.That(harness.ConfiguredRoomIds).IsEquivalentTo([ExistingRoomId, 5440], CollectionOrdering.Matching);
			// 批量查询结果里缺少的房间再单独确认一次
			await Assert.That(harness.RoomInfoQueries).Contains("404404");
		}
	}

	[Test]
	[Timeout(30_000)]
	public async Task AddRangeFallsBackToSingleLookupsWhenTheBatchIsRejectedAsync(CancellationToken cancellationToken)
	{
		await using Harness harness = await Harness.CreateAsync(rejectBatch: true);

		IReadOnlyList<RoomAddResult> results = await harness.Rooms.AddRangeAsync([Room(732), Room(1)], cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(results).All(static result => result is RoomAddResult.Added);
			await Assert.That(harness.ConfiguredRoomIds).IsEquivalentTo([ExistingRoomId, 6154037, 5440], CollectionOrdering.Matching);
		}
	}

	[Test]
	[Timeout(30_000)]
	public async Task AddRangeFailsTheChunkWithoutRetryingWhenTheRequestFailsAsync(CancellationToken cancellationToken)
	{
		await using Harness harness = await Harness.CreateAsync();
		harness.Handler.RoomInfoStatus = HttpStatusCode.PreconditionFailed;

		IReadOnlyList<RoomAddResult> results = await harness.Rooms.AddRangeAsync([Room(732), Room(1)], cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(results).IsEquivalentTo([new RoomAddResult.Failed(732, AppLanguage.Current.RequestRateLimited), new RoomAddResult.Failed(1, AppLanguage.Current.RequestRateLimited)], EqualityComparer<RoomAddResult>.Default, CollectionOrdering.Matching);
			await Assert.That(harness.Config.UpdateCount).IsEqualTo(0);
			await Assert.That(harness.Rooms.GetRooms()).HasSingleItem();
			await Assert.That(harness.RoomInfoQueries).IsEquivalentTo(["732,1"], CollectionOrdering.Matching);
		}
	}

	[Test]
	[Timeout(30_000)]
	public async Task AddRangeReportsATimeoutWithALocalizedMessageAsync(CancellationToken cancellationToken)
	{
		await using Harness harness = await Harness.CreateAsync();
		harness.Handler.TimesOut = true;

		IReadOnlyList<RoomAddResult> results = await harness.Rooms.AddRangeAsync([Room(732), Room(1)], cancellationToken);

		await Assert.That(results).IsEquivalentTo([new RoomAddResult.Failed(732, AppLanguage.Current.RequestTimedOut), new RoomAddResult.Failed(1, AppLanguage.Current.RequestTimedOut)], EqualityComparer<RoomAddResult>.Default, CollectionOrdering.Matching);
	}

	[Test]
	[Timeout(30_000)]
	public async Task FollowedRoomsAreMarkedAddedByRoomIdOrShortIdAsync(CancellationToken cancellationToken)
	{
		await using Harness harness = await Harness.CreateAsync();
		await harness.Rooms.AddRangeAsync([Room(6154037)], cancellationToken);
		harness.Handler.Following = """{"code":0,"data":{"totalPage":1,"list":[{"roomid":732,"uname":"Asaki大人","live_status":1},{"roomid":22907643,"uname":"已有"},{"roomid":5440,"uname":"官方","title":"t"}]}}""";

		List<FollowedRoomDto> rooms = await harness.Rooms.GetFollowedRoomsAsync(cancellationToken).ToListAsync(cancellationToken);

		FollowedRoomDto[] expected =
		[
			new(732, "Asaki大人", string.Empty, LiveStatus.Live, true),
			new(ExistingRoomId, "已有", string.Empty, LiveStatus.Idle, true),
			new(5440, "官方", "t", LiveStatus.Idle, false)
		];

		await Assert.That(rooms).IsEquivalentTo(expected, CollectionOrdering.Matching);
	}

	[Test]
	[Timeout(30_000)]
	public async Task FollowedRoomsRequireLoginAsync(CancellationToken cancellationToken)
	{
		await using Harness harness = await Harness.CreateAsync();
		harness.Handler.Following = """{"code":-101,"message":"账号未登录","ttl":1}""";

		await Assert.ThrowsAsync<LoginRequiredException>(async () => await harness.Rooms.GetFollowedRoomsAsync(cancellationToken).ToListAsync(cancellationToken));
	}

	[Test]
	[Timeout(30_000)]
	public async Task BlockedFollowedRoomsFailWithAReadableMessageAsync(CancellationToken cancellationToken)
	{
		await using Harness harness = await Harness.CreateAsync();
		harness.Handler.FollowingStatus = HttpStatusCode.PreconditionFailed;

		await Assert.ThrowsAsync<InvalidOperationException>(async () => await harness.Rooms.GetFollowedRoomsAsync(cancellationToken).ToListAsync(cancellationToken)).WithMessage(AppLanguage.Current.RequestRateLimited);
	}

	[Test]
	[Timeout(30_000)]
	public async Task TimedOutFollowedRoomsFailWithALocalizedMessageAsync(CancellationToken cancellationToken)
	{
		await using Harness harness = await Harness.CreateAsync();
		harness.Handler.TimesOut = true;

		await Assert.ThrowsAsync<TimeoutException>(async () => await harness.Rooms.GetFollowedRoomsAsync(cancellationToken).ToListAsync(cancellationToken)).WithMessage(AppLanguage.Current.RequestTimedOut);
	}

	[Test]
	[Timeout(30_000)]
	public async Task AddingAnUnknownRoomFailsWithALocalizedMessageAsync(CancellationToken cancellationToken)
	{
		await using Harness harness = await Harness.CreateAsync();

		using (Assert.Multiple())
		{
			await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Rooms.AddAsync(Room(404404), cancellationToken)).WithMessage(AppLanguage.Current.RoomNotFound.ToString(404404));
			await Assert.That(harness.Config.UpdateCount).IsEqualTo(0);
		}
	}

	[Test]
	[Timeout(30_000)]
	public async Task AddingARoomWhileBlockedFailsWithAReadableMessageAsync(CancellationToken cancellationToken)
	{
		await using Harness harness = await Harness.CreateAsync();
		harness.Handler.RoomInfoStatus = HttpStatusCode.PreconditionFailed;

		await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Rooms.AddAsync(Room(732), cancellationToken)).WithMessage(AppLanguage.Current.RequestRateLimited);
	}

	[Test]
	[Timeout(30_000)]
	public async Task AddingARoomThatTimesOutFailsWithALocalizedMessageAsync(CancellationToken cancellationToken)
	{
		await using Harness harness = await Harness.CreateAsync();
		harness.Handler.TimesOut = true;

		using (Assert.Multiple())
		{
			await Assert.ThrowsAsync<TimeoutException>(() => harness.Rooms.AddAsync(Room(732), cancellationToken)).WithMessage(AppLanguage.Current.RequestTimedOut);
			await Assert.That(harness.Config.UpdateCount).IsEqualTo(0);
		}
	}

	// 删除房间后马上退出：房间还在收尾时退出要等它，也要先确认
	[Test]
	[Timeout(30_000)]
	public async Task StopWaitsForRemovedRoomsThatAreStillClosingAsync(CancellationToken cancellationToken)
	{
		await using Harness harness = await Harness.CreateAsync();
		TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
		harness.Handler.RoomInfoGate = released.Task;

		// 改检查间隔会马上再查一次状态，这次查询卡住时房间要等它结束才释放完
		await harness.Rooms.UpdateAsync(Room(ExistingRoomId) with { StatusCheckInterval = TimeSpan.FromMinutes(1) }, cancellationToken);
		await TestWait.UntilAsync(() => harness.Handler.RoomInfoQueries.Contains(ExistingRoomId.ToString(CultureInfo.InvariantCulture)), "the status check is in flight", cancellationToken);

		Task removing = harness.Rooms.RemoveAsync([ExistingRoomId], cancellationToken);
		Task stopping = harness.Rooms.StopAsync();
		(bool stoppedEarly, bool isRecording) = (stopping.IsCompleted, harness.Rooms.IsRecording);

		released.SetResult();
		await Task.WhenAll(removing, stopping);

		using (Assert.Multiple())
		{
			await Assert.That(stoppedEarly).IsFalse();
			await Assert.That(isRecording).IsTrue();
			await Assert.That(harness.Rooms.IsRecording).IsFalse();
		}
	}

	// 启动时在配置锁外枚举房间列表，这时界面上可能正在增删改房间
	[Test]
	[Timeout(30_000)]
	[MethodDataSource(nameof(RoomListChanges))]
	public async Task RoomChangesReplaceTheRoomListInsteadOfChangingItAsync(Func<IRoomAppService, CancellationToken, Task> change, RoomSettingsDto[] expected, CancellationToken cancellationToken)
	{
		await using Harness harness = await Harness.CreateWithRoomsAsync(cancellationToken);
		List<RoomConfig> listed = harness.AppConfig.Rooms;
		RoomConfig[] original = [.. listed];

		await change(harness.Rooms, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(listed).IsEquivalentTo(original, ReferenceEqualityComparer.Instance, CollectionOrdering.Matching);
			await Assert.That(harness.AppConfig.Rooms.Select(static room => room.ToDto())).IsEquivalentTo(expected, CollectionOrdering.Matching);
		}
	}

	public static IEnumerable<TestDataRow<Func<(Func<IRoomAppService, CancellationToken, Task> Change, RoomSettingsDto[] Expected)>>> RoomListChanges()
	{
		// 初始顺序：已有、第一、第二、第三、第四
		yield return new(static () => (static (rooms, cancellationToken) => rooms.AddAsync(Room(FifthRoomId), cancellationToken), Rooms(ExistingRoomId, FirstRoomId, SecondRoomId, ThirdRoomId, FourthRoomId, FifthRoomId)), DisplayName: "Add");
		yield return new(static () => (static (rooms, cancellationToken) => rooms.AddRangeAsync([Room(FifthRoomId)], cancellationToken), Rooms(ExistingRoomId, FirstRoomId, SecondRoomId, ThirdRoomId, FourthRoomId, FifthRoomId)), DisplayName: "AddRange");
		yield return new(static () => (static (rooms, cancellationToken) => rooms.UpdateAsync(Room(FirstRoomId) with { IsRecordingEnabled = false }, cancellationToken), [Room(ExistingRoomId), Room(FirstRoomId) with { IsRecordingEnabled = false }, Room(SecondRoomId), Room(ThirdRoomId), Room(FourthRoomId)]), DisplayName: "Update");
		yield return new(static () => (static (rooms, cancellationToken) => rooms.MoveAsync([FourthRoomId], ExistingRoomId, cancellationToken), Rooms(FourthRoomId, ExistingRoomId, FirstRoomId, SecondRoomId, ThirdRoomId)), DisplayName: "Move");
		yield return new(static () => (static (rooms, cancellationToken) => rooms.RemoveAsync([FirstRoomId], cancellationToken), Rooms(ExistingRoomId, SecondRoomId, ThirdRoomId, FourthRoomId)), DisplayName: "Remove");

		static RoomSettingsDto[] Rooms(params long[] roomIds)
		{
			return [.. roomIds.Select(Room)];
		}
	}

	[Test]
	[Timeout(30_000)]
	[MethodDataSource(nameof(MoveCases))]
	public async Task MoveKeepsTheRelativeOrderAndWritesConfigOnceAsync(long[] roomIds, long? before, long[] expected, CancellationToken cancellationToken)
	{
		await using Harness harness = await Harness.CreateWithRoomsAsync(cancellationToken);
		(int updates, long version) = (harness.Config.UpdateCount, harness.Rooms.Version);

		await harness.Rooms.MoveAsync(roomIds, before, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(harness.Rooms.GetRooms().Select(static room => room.RoomId)).IsEquivalentTo(expected, CollectionOrdering.Matching);
			await Assert.That(harness.ConfiguredRoomIds).IsEquivalentTo(expected, CollectionOrdering.Matching);
			await Assert.That(harness.Config.UpdateCount).IsEqualTo(updates + 1);
			await Assert.That(harness.Rooms.Version).IsGreaterThan(version);
		}
	}

	public static IEnumerable<Func<(long[] RoomIds, long? Before, long[] Expected)>> MoveCases()
	{
		// 初始顺序：已有、第一、第二、第三、第四
		yield return () => ([ThirdRoomId, FirstRoomId], ExistingRoomId, [FirstRoomId, ThirdRoomId, ExistingRoomId, SecondRoomId, FourthRoomId]);
		yield return () => ([ExistingRoomId, SecondRoomId], null, [FirstRoomId, ThirdRoomId, FourthRoomId, ExistingRoomId, SecondRoomId]);
		yield return () => ([FourthRoomId], SecondRoomId, [ExistingRoomId, FirstRoomId, FourthRoomId, SecondRoomId, ThirdRoomId]);
		// 目标也在移动的房间里时，以它之后第一个不移动的房间为准
		yield return () => ([FirstRoomId, ThirdRoomId], FirstRoomId, [ExistingRoomId, FirstRoomId, ThirdRoomId, SecondRoomId, FourthRoomId]);
		yield return () => ([SecondRoomId, 404404], ExistingRoomId, [SecondRoomId, ExistingRoomId, FirstRoomId, ThirdRoomId, FourthRoomId]);
	}

	[Test]
	[Timeout(30_000)]
	[MethodDataSource(nameof(UnchangedMoveCases))]
	public async Task MoveThatChangesNothingDoesNotWriteConfigAsync(long[] roomIds, long? before, CancellationToken cancellationToken)
	{
		await using Harness harness = await Harness.CreateWithRoomsAsync(cancellationToken);
		long[] original = harness.ConfiguredRoomIds;
		int updates = harness.Config.UpdateCount;

		await harness.Rooms.MoveAsync(roomIds, before, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(harness.Rooms.GetRooms().Select(static room => room.RoomId)).IsEquivalentTo(original, CollectionOrdering.Matching);
			await Assert.That(harness.ConfiguredRoomIds).IsEquivalentTo(original, CollectionOrdering.Matching);
			await Assert.That(harness.Config.UpdateCount).IsEqualTo(updates);
		}
	}

	public static IEnumerable<Func<(long[] RoomIds, long? Before)>> UnchangedMoveCases()
	{
		yield return () => ([ThirdRoomId, FourthRoomId], null);
		yield return () => ([ExistingRoomId, FirstRoomId], SecondRoomId);
		// 目标和它之后的房间都在移动，等同于移到末尾
		yield return () => ([ThirdRoomId, FourthRoomId], ThirdRoomId);
		yield return () => ([404404], ExistingRoomId);
		yield return () => ([FirstRoomId], 404404);
		yield return () => ([], null);
	}

	private static RoomSettingsDto Room(long roomId)
	{
		return new RoomSettingsDto { RoomId = roomId };
	}

	private sealed class Harness : IAsyncDisposable
	{
		private readonly ServiceProvider _services;

		private Harness(ServiceProvider services, AppConfig config, FakeConfigManager configManager, FakeBilibiliHandler handler)
		{
			_services = services;
			AppConfig = config;
			Config = configManager;
			Handler = handler;
			Rooms = services.GetRequiredService<RoomAppService>();
		}

		public AppConfig AppConfig { get; }

		public FakeConfigManager Config { get; }

		public FakeBilibiliHandler Handler { get; }

		public RoomAppService Rooms { get; }

		public long[] ConfiguredRoomIds => [.. AppConfig.Rooms.Select(static room => room.RoomId)];

		public string[] RoomInfoQueries => [.. Handler.RoomInfoQueries];

		public static async Task<Harness> CreateWithRoomsAsync(CancellationToken cancellationToken)
		{
			Harness harness = await CreateAsync();
			await harness.Rooms.AddRangeAsync([Room(FirstRoomId), Room(SecondRoomId), Room(ThirdRoomId), Room(FourthRoomId)], cancellationToken);
			return harness;
		}

		public static async Task<Harness> CreateAsync(bool rejectBatch = false)
		{
			AppConfig config = AppConfig.CreateDefault();
			config.Rooms.Add(new RoomConfig { RoomId = ExistingRoomId });
			FakeConfigManager configManager = new(config);
			FakeBilibiliHandler handler = new() { RejectBatch = rejectBatch };

			ServiceProvider services = new ServiceCollection()
				.AddLogging()
				.AddSingleton<TimeProvider>(new FakeTimeProvider())
				.AddSingleton<IAppConfigManager>(configManager)
				.AddSingleton<INotificationService, RecordingNotifications>()
				.AddApplication()
				.AddHttpClient(HttpClientNames.BilibiliApi)
				.ConfigurePrimaryHttpMessageHandler(() => handler)
				.Services
				.BuildServiceProvider();

			Harness harness = new(services, config, configManager, handler);
			await harness.Rooms.StartAsync();
			await harness.WaitForStartupRefreshAsync();
			return harness;
		}

		// 已有房间启动时的首次刷新不算在用例的查询里
		private async Task WaitForStartupRefreshAsync()
		{
			await TestWait.UntilAsync(() => Handler.RoomInfoQueries.Contains(ExistingRoomId.ToString(CultureInfo.InvariantCulture)), "the existing room is refreshed at startup", CancellationToken.None);

			Handler.RoomInfoQueries.Clear();
		}

		public async ValueTask DisposeAsync()
		{
			await Rooms.StopAsync();
			await _services.DisposeAsync();
		}
	}

	private sealed class FakeBilibiliHandler : HttpMessageHandler
	{
		public ConcurrentQueue<string> RoomInfoQueries { get; } = new();

		public bool RejectBatch { get; init; }

		public HttpStatusCode RoomInfoStatus { get; set; } = HttpStatusCode.OK;

		public string Following { get; set; } = """{"code":0,"data":{"totalPage":0,"list":[]}}""";

		public HttpStatusCode FollowingStatus { get; set; } = HttpStatusCode.OK;

		// HttpClient 超时抛的是带着 TimeoutException 的 TaskCanceledException
		public bool TimesOut { get; set; }

		// 给出时房间信息的应答等它完成，不理会取消
		public Task? RoomInfoGate { get; set; }

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			if (TimesOut)
			{
				throw new TaskCanceledException(null, new TimeoutException());
			}

			Uri uri = request.RequestUri ?? throw new InvalidOperationException("The request has no uri.");

			switch (uri.AbsolutePath)
			{
				case "/xlive/web-room/v1/index/getRoomBaseInfo":
				{
					HttpResponseMessage response = RespondRoomInfo(HttpUtility.ParseQueryString(uri.Query).GetValues("room_ids") ?? []);

					if (RoomInfoGate is { } gate)
					{
						await gate;
					}

					return response;
				}
				case "/xlive/web-ucenter/user/following":
				{
					return FollowingStatus is HttpStatusCode.OK ? Json(Following) : new HttpResponseMessage(FollowingStatus);
				}
				default:
				{
					return new HttpResponseMessage(HttpStatusCode.NotFound);
				}
			}
		}

		private HttpResponseMessage RespondRoomInfo(string[] roomIds)
		{
			RoomInfoQueries.Enqueue(string.Join(',', roomIds));

			if (RoomInfoStatus is not HttpStatusCode.OK)
			{
				return new HttpResponseMessage(RoomInfoStatus);
			}

			if (RejectBatch && roomIds.Length > 1)
			{
				return Json("""{"code":-400,"message":"请求错误","data":[]}""");
			}

			long[] requested = [.. roomIds.Select(static id => long.Parse(id, CultureInfo.InvariantCulture))];
			IEnumerable<string> rooms = KnownRooms
				.Where(room => requested.Any(id => id == room.RoomId || id == room.ShortId))
				.Select(static room => $$"""
					"{{room.RoomId}}":{"room_id":{{room.RoomId}},"short_id":{{room.ShortId}},"uid":1,"uname":"{{room.UserName}}","title":"","live_status":0}
					""");

			return Json("""{"code":0,"message":"OK","data":{"by_uids":{},"by_room_ids":{""" + string.Join(',', rooms) + "}}}");
		}

		private static HttpResponseMessage Json(string json)
		{
			return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
		}
	}
}
