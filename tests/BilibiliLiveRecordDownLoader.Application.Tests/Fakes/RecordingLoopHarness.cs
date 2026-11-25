using BilibiliLive;
using BilibiliLive.Models;
using BilibiliLiveRecordDownLoader.Application.Contracts.Media;
using BilibiliLiveRecordDownLoader.Application.Contracts.Storage;
using BilibiliLiveRecordDownLoader.Application.Recording;
using BilibiliLiveRecordDownLoader.Application.Storage;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Pipelines;
using System.Net;
using System.Text;
using System.Threading.Channels;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Fakes;

internal sealed class RecordingLoopHarness : IAsyncDisposable
{
	public const long RoomId = 22907643;

	public static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(6);

	private const string RoomInfoTemplate = """{"code":0,"data":{"by_room_ids":{"$ROOM":{"room_id":$ROOM,"uid":1,"live_status":$LIVE,"title":"$TITLE","short_id":0,"uname":"streamer","area_name":"area","parent_area_name":"parent area","live_time":"2026-01-01 07:59:00","live_id_str":"745"}}}}""";

	private const string PlayInfoTemplate = """{"code":0,"data":{"room_id":$ROOM,"live_status":$LIVE,"playurl_info":{"playurl":{"stream":$STREAMS,"g_qn_desc":$QN_DESC}}}}""";

	private const string DefaultStreams = """
		[{"protocol_name":"http_stream","format":[{"format_name":"flv","codec":[
			{"codec_name":"avc","current_qn":10000,"base_url":"/live/avc.flv","url_info":[{"host":"https://a.test","extra":"?sign=$SIGN"},{"host":"https://b.test","extra":"?sign=$SIGN"}]},
			{"codec_name":"av1","current_qn":10000,"base_url":"/live/av1.flv","url_info":[{"host":"https://c.test","extra":"?sign=$SIGN"}]}
		]}]}]
		""";

	private readonly TestDirectory _directory = new();
	private readonly Channel<StreamRequest> _streams = Channel.CreateUnbounded<StreamRequest>();
	private readonly CancellationTokenSource _lifetime;
	private readonly RecordingPostProcessor _postProcessor;
	private readonly Func<HttpRequestMessage, HttpResponseMessage>? _respondStream;
	private int _playInfoRequests;
	private volatile bool _isLive = true;
	private RoomConfig _settings;

	/// <param name="streams">其中的 $SIGN 换成第几次请求候选流</param>
	public RecordingLoopHarness(CancellationToken cancellationToken, Func<string, Stream>? createFile = null, bool isNotificationEnabled = false, IMediaToolAppService? mediaTools = null, string streams = DefaultStreams, Func<HttpRequestMessage, HttpResponseMessage>? respondStream = null)
	{
		_lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		Streams = streams;
		_respondStream = respondStream;
		Config.RecordDirectory = _directory.Root;

		FakeConfigManager configManager = new(Config);

		BilibiliApiClient apiClient = new(new HttpClient(new Handler(RespondApiAsync)), Time);
		LiveRecorder recorder = new(apiClient, new StreamClientFactory(new Handler(RespondStreamAsync)), configManager, Time, new CapturingLogger<LiveRecorder>(Logs, RecorderLogs))
		{
			CreateFile = createFile ?? LiveRecorder.CreateRecordingFile,
			DrainTimeout = TimeSpan.FromSeconds(1)
		};

		Danmu = new FakeDanmuSource(Time);
		_postProcessor = new RecordingPostProcessor(mediaTools ?? new UnusedMediaTools(), configManager, Notifications, new CapturingLogger<RecordingPostProcessor>(Logs));
		RoomSessionServices services = new
		(
			apiClient,
			Danmu.ReceiveAsync,
			recorder,
			_postProcessor,
			new LowDiskSpaceMonitor(new UnknownDiskSpace(), configManager, Notifications, Time, new CapturingLogger<LowDiskSpaceMonitor>(Logs)),
			Notifications,
			configManager,
			Time,
			new CapturingLogger<RoomSession>(Logs)
		);

		// 测试里的接口数据和断言都按选了原画来写
		_settings = new RoomConfig { RoomId = RoomId, IsNotificationEnabled = isNotificationEnabled, Quality = StreamQuality.Original, StreamReconnectDelay = ReconnectDelay };
		Session = new RoomSession(_settings, services, static () => { });
	}

	public FakeTimeProvider Time { get; } = new();

	public AppConfig Config { get; } = AppConfig.CreateDefault();

	public string Streams { get => Volatile.Read(ref field); set => Volatile.Write(ref field, value); }

	public string QualityDescriptions { get => Volatile.Read(ref field); set => Volatile.Write(ref field, value); } = "[]";

	public string Title { get => Volatile.Read(ref field); set => Volatile.Write(ref field, value); } = "title";

	public RecordingNotifications Notifications { get; } = new();

	public FakeDanmuSource Danmu { get; }

	public string RoomDirectory => RecordingFileLayout.GetRoomDirectory(Config.RecordDirectory, RoomId);

	public RoomConfig Settings
	{
		get => _settings;
		set
		{
			_settings = value;
			Session.UpdateSettings(value);
		}
	}

	public RoomSession Session { get; }

	public RoomSnapshot Snapshot => Session.GetSnapshot();

	public int PlayInfoRequests => Volatile.Read(ref _playInfoRequests);

	public string? PlayInfoQuery { get => Volatile.Read(ref field); private set => Volatile.Write(ref field, value); }

	public Func<HttpRequestMessage, HttpResponseMessage>? MasterPlaylist { get; set; }

	public Func<HttpRequestMessage, HttpResponseMessage>? RoomInfo { get; set; }

	public ConcurrentQueue<string> MasterPlaylistQueries { get; } = new();

	public ConcurrentQueue<string> RecorderLogs { get; } = new();

	public ConcurrentQueue<HarnessLog> Logs { get; } = new();

	public ConcurrentQueue<Uri> StreamRequests { get; } = new();

	public void GoLive()
	{
		_isLive = true;
		Session.Apply(new LiveRoomInfo(RoomId, 0, 1, "streamer", Title, LiveStatus.Live));
	}

	public void GoOffline()
	{
		_isLive = false;
	}

	public void StartStreaming()
	{
		_isLive = true;
	}

	public Task<StreamRequest> NextStreamAsync()
	{
		return TestWait.ForAsync(_streams.Reader.ReadAsync, "the room requests a stream", _lifetime.Token);
	}

	public static RecordingLoopHarness CreateSplitting(IMediaToolAppService mediaTools, CancellationToken cancellationToken, Func<string, Stream>? createFile = null)
	{
		RecordingLoopHarness loop = new(cancellationToken, createFile, mediaTools: mediaTools);
		loop.Time.SetUtcNow(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
		loop.Time.SetLocalTimeZone(TimeZoneInfo.Utc);
		loop.Config.SplitMode = RecordingSplitMode.Duration;
		loop.Config.SplitDuration = TimeSpan.FromMinutes(1);
		loop.Config.ConvertToMp4 = true;
		loop.Config.IncludeTitleInFileName = false;

		// 一次推进一分多钟，不能被当成空闲
		loop.Settings = loop.Settings with { StreamIdleTimeout = TimeSpan.FromHours(1) };
		return loop;
	}

	public Task WaitUntilAsync(Func<RoomSnapshot, bool> condition)
	{
		return WaitUntilAsync(() => condition(Snapshot), "the room snapshot matches");
	}

	public Task WaitUntilAsync(Func<bool> condition, string because)
	{
		return TestWait.UntilAsync(condition, because, _lifetime.Token);
	}

	// 按步推进，不依赖重连等待何时注册定时器
	public async Task<(StreamRequest Request, TimeSpan Advanced)> AdvanceUntilNextStreamAsync(TimeSpan? interval = null)
	{
		TimeSpan advance = interval ?? TimeSpan.FromSeconds(1);
		TimeSpan advanced = TimeSpan.Zero;

		for (int step = 0; step < 60; ++step)
		{
			if (await WaitForStreamAsync(TimeSpan.FromMilliseconds(100)) is { } request)
			{
				return (request, advanced);
			}

			Time.Advance(advance);
			advanced += advance;
		}

		throw new TimeoutException("No stream was requested.");
	}

	public async Task<(FakeDanmuConnection Connection, TimeSpan Advanced)> AdvanceUntilDanmuReconnectedAsync()
	{
		// 先等房间察觉断开再推进，推进的时长才都算在重连等待里
		await WaitUntilAsync(() => Session.Danmaku.Feed.Status.DisconnectedAt is not null, "the room notices the disconnection");
		Task<FakeDanmuConnection> next = Danmu.NextConnectionAsync();
		TimeSpan advanced = TimeSpan.Zero;

		for (int step = 0; step < 20 && !next.IsCompleted; ++step)
		{
			// 留出注册重连定时器的时间
			await Task.Delay(50, _lifetime.Token);

			if (!next.IsCompleted)
			{
				Time.Advance(TimeSpan.FromSeconds(1));
				advanced += TimeSpan.FromSeconds(1);
			}
		}

		return (await next, advanced);
	}

	public async Task<RoomSnapshot> RecordForAsync(StreamRequest request, TimeSpan duration)
	{
		RoomSnapshot recording = await ReceiveForAsync(request, duration);
		await request.EndAsync();
		return recording;
	}

	public async Task<RoomSnapshot> ReceiveForAsync(StreamRequest request, TimeSpan duration)
	{
		await request.AcceptAsync();
		await request.CopyStarted;
		RoomSnapshot recording = Snapshot;
		Time.Advance(duration);

		// 推进的终点也送一块数据，录到的时长才不依赖看门狗的采样时刻
		await request.SendAndWaitAsync(new byte[1024]);

		// 时钟一次跨过多个采样周期时速度会是 NaN，所以不等正数
		await WaitUntilAsync(static snapshot => snapshot.BytesPerSecond is not 0);
		return recording;
	}

	public async Task StopAsync()
	{
		await Session.DisposeAsync();
		await _postProcessor.WhenIdleAsync();
	}

	public async ValueTask DisposeAsync()
	{
		// 后处理还开着或正在删除录制目录里的文件时删除目录会失败
		await StopAsync();
		await _lifetime.CancelAsync();
		_lifetime.Dispose();
		_directory.Dispose();
	}

	private async Task<StreamRequest?> WaitForStreamAsync(TimeSpan timeout)
	{
		using CancellationTokenSource wait = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
		wait.CancelAfter(timeout);

		try
		{
			return await _streams.Reader.ReadAsync(wait.Token);
		}
		catch (OperationCanceledException) when (!_lifetime.IsCancellationRequested)
		{
			return null;
		}
	}

	private Task<HttpResponseMessage> RespondApiAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		if (request.RequestUri is { AbsolutePath: "/xlive/web-room/v2/index/getRoomPlayInfo", Query: var query })
		{
			PlayInfoQuery = query;
		}

		if (request.RequestUri is { AbsolutePath: "/xlive/play-gateway/master/url", Query: var masterQuery })
		{
			MasterPlaylistQueries.Enqueue(masterQuery);
			return Task.FromResult(MasterPlaylist?.Invoke(request) ?? new HttpResponseMessage(HttpStatusCode.NotFound));
		}

		if (request.RequestUri is { AbsolutePath: "/xlive/web-room/v1/index/getRoomBaseInfo" } && RoomInfo is { } respondRoomInfo)
		{
			return Task.FromResult(respondRoomInfo(request));
		}

		string template = request.RequestUri?.AbsolutePath switch
		{
			"/xlive/web-room/v2/index/getRoomPlayInfo" => PlayInfoTemplate.Replace("$STREAMS", Streams, StringComparison.Ordinal).Replace("$QN_DESC", QualityDescriptions, StringComparison.Ordinal).Replace("$SIGN", Interlocked.Increment(ref _playInfoRequests).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal),
			"/xlive/web-room/v1/index/getRoomBaseInfo" => RoomInfoTemplate,
			var path => throw new InvalidOperationException($"Unexpected API request {path}.")
		};
		string json = template.Replace("$ROOM", RoomId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal).Replace("$LIVE", _isLive ? "1" : "0", StringComparison.Ordinal).Replace("$TITLE", Title, StringComparison.Ordinal);

		return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
	}

	private async Task<HttpResponseMessage> RespondStreamAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		if (request.RequestUri is { } uri)
		{
			StreamRequests.Enqueue(uri);
		}

		if (_respondStream is not null)
		{
			return _respondStream(request);
		}

		StreamRequest stream = new(request.RequestUri?.Host ?? string.Empty);
		await _streams.Writer.WriteAsync(stream, cancellationToken);
		return await stream.Response.WaitAsync(cancellationToken);
	}

	private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			return respond(request, cancellationToken);
		}
	}

	private sealed class CapturingLogger<T>(ConcurrentQueue<HarnessLog> logs, ConcurrentQueue<string>? messages = null) : ILogger<T>
	{
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
			string message = formatter(state, exception);
			logs.Enqueue(new HarnessLog(logLevel, message, exception));
			messages?.Enqueue(message);
		}
	}

	private sealed class StreamClientFactory(HttpMessageHandler handler) : IHttpClientFactory
	{
		public HttpClient CreateClient(string name)
		{
			return new HttpClient(handler, false);
		}
	}

	private sealed class UnknownDiskSpace : IDiskSpaceService
	{
		public DiskSpaceInfo? GetDiskSpace(string path)
		{
			return null;
		}
	}

	private sealed class UnusedMediaTools : MediaToolAppServiceStub;
}

internal sealed record HarnessLog(LogLevel Level, string Message, Exception? Exception);

internal sealed class StreamRequest(string host)
{
	private readonly TaskCompletionSource<HttpResponseMessage> _response = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly Pipe _body = new();
	private readonly TaskCompletionSource _copyStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private int _reads;

	public string Host { get; } = host;

	public Task<HttpResponseMessage> Response => _response.Task;

	// 读过 FLV 头之后才完成，这时空闲看门狗已经在运行
	public Task CopyStarted => _copyStarted.Task;

	public async Task AcceptAsync(bool sendFlv = true)
	{
		if (sendFlv)
		{
			await _body.Writer.WriteAsync((byte[])[.. "FLV"u8, .. new byte[1024]]);
		}
		else
		{
			await _body.Writer.CompleteAsync();
		}

		Respond();
	}

	public async Task AcceptAsync(byte[] body)
	{
		await _body.Writer.WriteAsync(body);
		Respond();

		// 一次读 FLV 签名，一次读其余数据，第三次读说明前面的都处理完了
		await WaitForReadsAsync(3);
	}

	// 会话开始读下一块时才返回，之后推进的时钟不会算到这块数据上
	public async Task SendAndWaitAsync(byte[] data)
	{
		int reads = Volatile.Read(ref _reads);
		await _body.Writer.WriteAsync(data);
		await WaitForReadsAsync(reads + 1);
	}

	// 404 是 CDN 上没有这路流，主播没在推流时就是这样
	public void Reject(HttpStatusCode status = HttpStatusCode.NotFound)
	{
		_response.SetResult(new HttpResponseMessage(status));
	}

	public async Task SendAsync()
	{
		await _body.Writer.WriteAsync(new byte[1024]);
	}

	public async Task StreamUntilClosedAsync()
	{
		byte[] chunk = new byte[64 << 10];
		FlushResult result;

		do
		{
			result = await _body.Writer.WriteAsync(chunk);
		}
		while (!result.IsCompleted);
	}

	public async Task EndAsync()
	{
		await _body.Writer.CompleteAsync();
	}

	private void Respond()
	{
		_response.SetResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new ObservedStream(_body.Reader.AsStream(), OnRead)) });
	}

	private Task WaitForReadsAsync(int count)
	{
		return TestWait.UntilAsync(() => Volatile.Read(ref _reads) >= count, $"the session reads the stream {count} times", CancellationToken.None);
	}

	private void OnRead()
	{
		if (Interlocked.Increment(ref _reads) is 2)
		{
			_copyStarted.TrySetResult();
		}
	}

	private sealed class ObservedStream(Stream inner, Action onRead) : Stream
	{
		public override bool CanRead => true;

		public override bool CanSeek => false;

		public override bool CanWrite => false;

		public override long Length => throw new NotSupportedException();

		public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

		public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
		{
			onRead();
			return inner.ReadAsync(buffer, cancellationToken);
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			throw new NotSupportedException();
		}

		public override void Flush()
		{
		}

		public override long Seek(long offset, SeekOrigin origin)
		{
			throw new NotSupportedException();
		}

		public override void SetLength(long value)
		{
			throw new NotSupportedException();
		}

		public override void Write(byte[] buffer, int offset, int count)
		{
			throw new NotSupportedException();
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				inner.Dispose();
			}

			base.Dispose(disposing);
		}
	}
}
