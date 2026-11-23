using BilibiliLive.Danmu;
using BilibiliLiveRecordDownLoader.Application.Contracts.Platform;
using BilibiliLiveRecordDownLoader.Application.Danmaku;
using BilibiliLiveRecordDownLoader.Application.Http;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Threading.Channels;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class RoomDanmuLoopTest
{
	private const long RoomId = 22907643;

	private static readonly DanmuSender Viewer = new(42, "观众昵称", GuardLevel.None, null);

	[Test]
	public async Task ConsecutiveDanmuFailuresBackOffExponentiallyAsync(CancellationToken cancellationToken)
	{
		FakeTimeProvider time = new();
		ReconnectDelayLog log = new();
		await using ServiceProvider services = new ServiceCollection()
			.AddLogging(builder => builder.AddProvider(log))
			.AddSingleton<TimeProvider>(time)
			.AddSingleton<IAppConfigManager, FakeConfigManager>()
			.AddSingleton<INotificationService, RecordingNotifications>()
			.AddApplication()
			.AddHttpClient(HttpClientNames.BilibiliApi)
			.ConfigurePrimaryHttpMessageHandler(static () => new UnavailableHandler())
			.Services
			.BuildServiceProvider();
		RoomConfig settings = new() { RoomId = RoomId };
		await using RoomSession session = services.GetRequiredService<RoomSessionFactory>().Create(settings, static () => { });

		session.Start();
		List<TimeSpan> delays = [await log.Delays.ReadAsync(cancellationToken)];

		while (delays.Count < 4)
		{
			// 定时器可能还没注册，没等到下一次失败就再推进一次
			time.Advance(delays[^1]);

			using CancellationTokenSource wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			wait.CancelAfter(TimeSpan.FromMilliseconds(200));

			try
			{
				delays.Add(await log.Delays.ReadAsync(wait.Token));
			}
			catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
			{
			}
		}

		using (Assert.Multiple())
		{
			for (int i = 0; i < delays.Count; ++i)
			{
				TimeSpan expected = settings.DanmuReconnectDelay * (1 << i);
				await Assert.That(delays[i]).IsBetween(expected * ReconnectBackoff.MinJitter, expected * ReconnectBackoff.MaxJitter);
			}
		}
	}

	[Test]
	public async Task ChangingTheMonitorIntervalsKeepsTheDanmuConnectionAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken);
		FakeDanmuConnection danmu = await ConnectAsync(loop);

		loop.Session.UpdateSettings(loop.Settings with { StatusCheckInterval = loop.Settings.StatusCheckInterval * 2, DanmuReconnectDelay = loop.Settings.DanmuReconnectDelay * 2 });

		// 连接被断开时这条消息不会再被处理，发送会超时
		await danmu.SendAsync(Comment(Viewer, "改设置之后"));

		await Assert.That(loop.Session.Danmaku.Feed.Status.DisconnectedAt).IsNull();
	}

	// 未登录的连接认证后马上收到这几条，存原始消息时未登录通知还多一份原始消息
	[Test]
	public async Task ConnectionsDroppedRightAfterAuthenticationKeepBackingOffAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken);

		// 未开播，推进时钟时只有弹幕连接在等
		loop.GoOffline();
		loop.Session.Start();
		FakeDanmuConnection danmu = await loop.Danmu.NextConnectionAsync();
		TimeSpan waited = TimeSpan.Zero;

		for (int i = 0; i < 2; ++i)
		{
			await danmu.SendAsync(new DanmuMessage.Connected(0));
			await danmu.SendAsync(new DanmuMessage.Raw("LOG_IN_NOTICE", """{"cmd":"LOG_IN_NOTICE"}"""u8.ToArray()));
			await danmu.SendAsync(new DanmuMessage.LoginNotice());
			danmu.Close();
			(danmu, waited) = await loop.AdvanceUntilDanmuReconnectedAsync();
		}

		await Assert.That(waited).IsGreaterThanOrEqualTo(loop.Settings.DanmuReconnectDelay * 2 * ReconnectBackoff.MinJitter);
	}

	[Test]
	public async Task LiveStartedFromDanmuDoesNotRecordThePreviousLiveIdAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken);
		loop.Settings.IsDanmakuEnabled = true;

		// 未开播时查到的 live_id 是上一场的
		loop.GoOffline();
		FakeDanmuConnection danmu = await ConnectAsync(loop);
		await loop.WaitUntilAsync(static snapshot => snapshot.LiveStatus is LiveStatus.Idle);

		loop.StartStreaming();
		await danmu.SendAsync(new DanmuMessage.LiveStarted());
		await StartRecordingAsync(loop);
		await loop.StopAsync();

		string path = await Assert.That(Directory.GetFiles(loop.RoomDirectory, "*.xml")).HasSingleItem();
		DanmakuFile file = await DanmakuXmlReader.ReadFileAsync(path, cancellationToken);

		await Assert.That(file.Source)
			.IsTypeOf<DanmakuFileSource.Native>()
			.And.Member(static source => source.Header.LiveId, static liveId => liveId.IsEmpty())
			.And.Member(static source => source.Header.LiveStartTime, static startTime => startTime.IsNull());
	}

	[Test]
	public async Task StoppingWaitsForThePostProcessingOfTheLastSegmentAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken);
		loop.Settings.IsDanmakuEnabled = true;
		FakeDanmuConnection danmu = await ConnectAsync(loop);
		await StartRecordingAsync(loop);
		await danmu.SendAsync(Comment(Viewer, "最后一段"));

		// 和退出时一样，停下房间之后不推进时钟也不另外等待
		await loop.StopAsync();

		await Assert.That(Directory.GetFiles(loop.RoomDirectory, "*.ass")).HasSingleItem();
	}

	// 状态查询的应答已经收到、还没校验时房间被停下，风控的 412 也按取消算，停止照常收尾
	[Test]
	public async Task StatusCheckFailingAsTheRoomStopsDoesNotFailTheStopAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken);
		TaskCompletionSource<Task> stopping = new(TaskCreationOptions.RunContinuationsAsynchronously);
		loop.RoomInfo = _ => new HttpResponseMessage(HttpStatusCode.PreconditionFailed) { Content = new StoppingContent(() => stopping.TrySetResult(loop.Session.DisposeAsync().AsTask())) };

		loop.Session.Start();
		Task stopped = await stopping.Task.WaitAsync(cancellationToken);

		await Assert.That(async () => await stopped).ThrowsNothing();
	}

	[Test]
	public async Task DanmakuContentAndViewersStayOutOfTheLogsAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken);
		loop.Settings.IsDanmakuEnabled = true;
		FakeDanmuConnection danmu = await ConnectAsync(loop);
		await StartRecordingAsync(loop);

		await danmu.SendAsync(Comment(Viewer, "弹幕内容"));
		await danmu.SendAsync(new DanmuMessage.SuperChat(1, Viewer, "醒目留言内容", 30000, TimeSpan.FromSeconds(60)));
		await danmu.SendAsync(new DanmuMessage.Gift(Viewer, "礼物名称", 1, 1000, DanmuCoinType.Gold));
		await danmu.SendAsync(new DanmuMessage.GuardBought(Viewer, GuardLevel.Captain, 1, 198000, false));
		await loop.StopAsync();

		string[] logs = [.. loop.Logs.Select(static log => $"{log.Message} {log.Exception}")];
		string[] secrets = [Viewer.Name, "弹幕内容", "醒目留言内容", "礼物名称", "sign="];

		using (Assert.Multiple())
		{
			await Assert.That(logs).IsNotEmpty();
			await Assert.That(logs.Where(log => Array.Exists(secrets, secret => log.Contains(secret, StringComparison.Ordinal)))).IsEmpty();
		}
	}

	private static async Task<FakeDanmuConnection> ConnectAsync(RecordingLoopHarness loop)
	{
		loop.Session.Start();
		FakeDanmuConnection danmu = await loop.Danmu.NextConnectionAsync();
		await danmu.SendAsync(new DanmuMessage.Connected(0));
		return danmu;
	}

	// 录下的视频要超过后处理删除的下限
	private static async Task StartRecordingAsync(RecordingLoopHarness loop)
	{
		StreamRequest stream = await loop.NextStreamAsync();
		await stream.AcceptAsync([.. FlvSamples.Header, .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.SequenceHeader, 0), .. KeyFrame(0)]);
		loop.Time.Advance(TimeSpan.FromSeconds(1));
		await stream.SendAndWaitAsync(KeyFrame(1000));
	}

	private static byte[] KeyFrame(uint timestamp)
	{
		return FlvSamples.PaddedVideo(VideoPacket.KeyFrame, timestamp, 40 << 10);
	}

	private static DanmuMessage.Comment Comment(DanmuSender sender, string text)
	{
		return new DanmuMessage.Comment(sender, text, 1, 25, 0xFFFFFF, DateTimeOffset.UnixEpoch, "a16fe0dd", false);
	}

	private sealed class UnavailableHandler : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
		}
	}

	// 缓冲应答时停下房间，等这次请求也被取消之后照常读完，取消就落在收到应答和校验之间
	private sealed class StoppingContent(Action stop) : HttpContent
	{
		protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
		{
			stop();
			await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
			await stream.WriteAsync("{}"u8.ToArray(), CancellationToken.None);
		}

		protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
		{
			throw new NotSupportedException("The content is only buffered with a cancellation token.");
		}

		protected override bool TryComputeLength(out long length)
		{
			length = 0;
			return false;
		}
	}

	private sealed class ReconnectDelayLog : ILoggerProvider, ILogger
	{
		private readonly Channel<TimeSpan> _delays = Channel.CreateUnbounded<TimeSpan>();

		public ChannelReader<TimeSpan> Delays => _delays.Reader;

		public ILogger CreateLogger(string categoryName)
		{
			return this;
		}

		public IDisposable? BeginScope<TState>(TState state) where TState : notnull
		{
			return null;
		}

		public bool IsEnabled(LogLevel logLevel)
		{
			return true;
		}

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
		{
			if (eventId.Name is "LogDanmuFailed" && state is IEnumerable<KeyValuePair<string, object?>> properties && properties.FirstOrDefault(static p => p.Key == "Delay").Value is TimeSpan delay)
			{
				_delays.Writer.TryWrite(delay);
			}
		}

		public void Dispose()
		{
		}
	}
}
