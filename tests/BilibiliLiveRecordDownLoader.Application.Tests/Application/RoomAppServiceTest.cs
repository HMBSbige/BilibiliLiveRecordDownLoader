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

	private static readonly (long RoomId, long ShortId, string UserName)[] KnownRooms = [(FirstRoomId, 732, "Asaki大人"), (SecondRoomId, 1, "官方"), (ExistingRoomId, 0, "已有"), (ThirdRoomId, 0, "第三"), (FourthRoomId, 0, "第四")];

	[Test]
	[Timeout(30_000)]
	public async Task AddRangeResolvesShortIdsInOneLookupAndWritesConfigOnceAsync(CancellationToken cancellationToken)
	{
		await using Harness harness = await Harness.CreateAsync();

		IReadOnlyList<RoomAddResult> results = await harness.Rooms.AddRangeAsync([Room(732), Room(ExistingRoomId), Room(6154037), Room(1)], cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(results[0] is RoomAddResult.Added { RequestedRoomId: 732, Room: { RoomId: 6154037, ShortId: 732, UserName: "Asaki大人" } }).IsTrue();
			await Assert.That(results[1]).IsEqualTo(new RoomAddResult.Skipped(ExistingRoomId, ExistingRoomId));
			await Assert.That(results[2]).IsEqualTo(new RoomAddResult.Skipped(6154037, 6154037));
			await Assert.That(results[3] is RoomAddResult.Added { RequestedRoomId: 1, Room.RoomId: 5440 }).IsTrue();
			await Assert.That(harness.Config.UpdateCount).IsEqualTo(1);
			await Assert.That(harness.ConfiguredRoomIds).IsEquivalentTo([ExistingRoomId, 6154037, 5440], EqualityComparer<long>.Default, CollectionOrdering.Matching);
			await Assert.That(harness.Rooms.GetRooms().Select(static room => room.RoomId).ToArray()).IsEquivalentTo([ExistingRoomId, 6154037, 5440], EqualityComparer<long>.Default, CollectionOrdering.Matching);
			// 已有的房间不再查询，短号也不会再单独查一次
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
			await Assert.That(results[0] is RoomAddResult.Failed { RequestedRoomId: 404404, Error.Length: > 0 }).IsTrue();
			await Assert.That(results[1] is RoomAddResult.Failed { RequestedRoomId: 0 }).IsTrue();
			await Assert.That(results[2] is RoomAddResult.Added { RequestedRoomId: 5440 }).IsTrue();
			await Assert.That(harness.Config.UpdateCount).IsEqualTo(1);
			await Assert.That(harness.ConfiguredRoomIds).IsEquivalentTo([ExistingRoomId, 5440], EqualityComparer<long>.Default, CollectionOrdering.Matching);
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
			await Assert.That(results.All(static result => result is RoomAddResult.Added)).IsTrue();
			await Assert.That(harness.ConfiguredRoomIds).IsEquivalentTo([ExistingRoomId, 6154037, 5440], EqualityComparer<long>.Default, CollectionOrdering.Matching);
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
			await Assert.That(results.All(static result => result is RoomAddResult.Failed { Error: var error } && error == AppLanguage.Current.RequestRateLimited)).IsTrue();
			await Assert.That(harness.Config.UpdateCount).IsEqualTo(0);
			await Assert.That(harness.Rooms.GetRooms().Count).IsEqualTo(1);
			await Assert.That(harness.RoomInfoQueries).IsEquivalentTo(["732,1"], EqualityComparer<string>.Default, CollectionOrdering.Matching);
		}
	}

	[Test]
	[Timeout(30_000)]
	public async Task FollowedRoomsAreMarkedAddedByRoomIdOrShortIdAsync(CancellationToken cancellationToken)
	{
		await using Harness harness = await Harness.CreateAsync();
		await harness.Rooms.AddRangeAsync([Room(6154037)], cancellationToken);
		harness.Handler.Following = """{"code":0,"data":{"totalPage":1,"list":[{"roomid":732,"uname":"Asaki大人","live_status":1},{"roomid":22907643,"uname":"已有"},{"roomid":5440,"uname":"官方","title":"t"}]}}""";

		List<FollowedRoomDto> rooms = [];

		await foreach (FollowedRoomDto room in harness.Rooms.GetFollowedRoomsAsync(cancellationToken))
		{
			rooms.Add(room);
		}

		FollowedRoomDto[] expected =
		[
			new(732, "Asaki大人", string.Empty, LiveStatus.Live, true),
			new(ExistingRoomId, "已有", string.Empty, LiveStatus.Idle, true),
			new(5440, "官方", "t", LiveStatus.Idle, false)
		];

		await Assert.That(rooms).IsEquivalentTo(expected, EqualityComparer<FollowedRoomDto>.Default, CollectionOrdering.Matching);
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

		InvalidOperationException? exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await harness.Rooms.GetFollowedRoomsAsync(cancellationToken).ToListAsync(cancellationToken));

		await Assert.That(exception?.Message).IsEqualTo(AppLanguage.Current.RequestRateLimited);
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
			await Assert.That(harness.Rooms.GetRooms().Select(static room => room.RoomId).ToArray()).IsEquivalentTo(expected, EqualityComparer<long>.Default, CollectionOrdering.Matching);
			await Assert.That(harness.ConfiguredRoomIds).IsEquivalentTo(expected, EqualityComparer<long>.Default, CollectionOrdering.Matching);
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
			await Assert.That(harness.Rooms.GetRooms().Select(static room => room.RoomId).ToArray()).IsEquivalentTo(original, EqualityComparer<long>.Default, CollectionOrdering.Matching);
			await Assert.That(harness.ConfiguredRoomIds).IsEquivalentTo(original, EqualityComparer<long>.Default, CollectionOrdering.Matching);
			await Assert.That(harness.Config.UpdateCount).IsEqualTo(updates);
		}
	}

	public static IEnumerable<Func<(long[] RoomIds, long? Before)>> UnchangedMoveCases()
	{
		// 已经在目标位置
		yield return () => ([ThirdRoomId, FourthRoomId], null);
		yield return () => ([ExistingRoomId, FirstRoomId], SecondRoomId);
		// 目标和它之后的房间都在移动，等同于移到末尾
		yield return () => ([ThirdRoomId, FourthRoomId], ThirdRoomId);
		// 只有未知的房间号，或目标房间不存在
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
		private readonly AppConfig _config;

		private Harness(ServiceProvider services, AppConfig config, FakeConfigManager configManager, FakeBilibiliHandler handler)
		{
			_services = services;
			_config = config;
			Config = configManager;
			Handler = handler;
			Rooms = services.GetRequiredService<RoomAppService>();
		}

		public FakeConfigManager Config { get; }

		public FakeBilibiliHandler Handler { get; }

		public RoomAppService Rooms { get; }

		public long[] ConfiguredRoomIds => [.. _config.Rooms.Select(static room => room.RoomId)];

		// 每次查询房间信息时请求的 room_ids，逗号分隔
		public string[] RoomInfoQueries => [.. Handler.RoomInfoQueries];

		// 已有、第一、第二、第三、第四
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
			using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));

			while (!Handler.RoomInfoQueries.Contains(ExistingRoomId.ToString(CultureInfo.InvariantCulture)))
			{
				await Task.Delay(10, timeout.Token);
			}

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

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Uri uri = request.RequestUri ?? throw new InvalidOperationException("The request has no uri.");

			HttpResponseMessage response = uri.AbsolutePath switch
			{
				"/xlive/web-room/v1/index/getRoomBaseInfo" => RespondRoomInfo(HttpUtility.ParseQueryString(uri.Query).GetValues("room_ids") ?? []),
				"/xlive/web-ucenter/user/following" => FollowingStatus is HttpStatusCode.OK ? Json(Following) : new HttpResponseMessage(FollowingStatus),
				_ => new HttpResponseMessage(HttpStatusCode.NotFound)
			};

			return Task.FromResult(response);
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
