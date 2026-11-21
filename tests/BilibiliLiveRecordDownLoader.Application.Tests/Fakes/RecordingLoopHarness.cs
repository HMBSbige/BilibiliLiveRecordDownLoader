using BilibiliLive;
using BilibiliLive.Danmu;
using BilibiliLive.Http;
using BilibiliLive.Models;
using BilibiliLiveRecordDownLoader.Application.Contracts.Media;
using BilibiliLiveRecordDownLoader.Application.Contracts.Storage;
using BilibiliLiveRecordDownLoader.Application.Media;
using BilibiliLiveRecordDownLoader.Application.Recording;
using BilibiliLiveRecordDownLoader.Application.Storage;
using Microsoft.Extensions.Time.Testing;
using System.Globalization;
using System.IO.Pipelines;
using System.Net;
using System.Text;
using System.Threading.Channels;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Fakes;

/// <summary>
/// 用真实的 RoomSession 和 LiveRecorder 跑录制循环，B 站接口和直播流都由假的 HttpMessageHandler 应答，时间由 FakeTimeProvider 控制。
/// 候选流依次是 FLV/AVC（a.test、b.test）和 FLV/AV1（c.test）。用 FFmpeg 录制时，假的 FFmpeg 不理会参数，向标准输出写一行后退出。
/// </summary>
internal sealed class RecordingLoopHarness : IAsyncDisposable
{
	public const long RoomId = 22907643;

	public static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(6);

	private const string RoomInfoTemplate = """{"code":0,"data":{"by_room_ids":{"$ROOM":{"room_id":$ROOM,"uid":1,"live_status":$LIVE,"title":"title","short_id":0,"uname":"streamer"}}}}""";

	private const string PlayInfoTemplate = """
		{"code":0,"data":{"room_id":$ROOM,"live_status":$LIVE,"playurl_info":{"playurl":{"stream":[{"protocol_name":"http_stream","format":[{"format_name":"flv","codec":[
			{"codec_name":"avc","current_qn":10000,"base_url":"/live/avc.flv","url_info":[{"host":"https://a.test","extra":"?sign=$SIGN"},{"host":"https://b.test","extra":"?sign=$SIGN"}]},
			{"codec_name":"av1","current_qn":10000,"base_url":"/live/av1.flv","url_info":[{"host":"https://c.test","extra":"?sign=$SIGN"}]}
		]}]}]}}}}
		""";

	private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

	private readonly TestDirectory _directory = new();
	private readonly Channel<StreamRequest> _streams = Channel.CreateUnbounded<StreamRequest>();
	private readonly CancellationTokenSource _lifetime;
	private int _playInfoRequests;
	private volatile bool _isLive = true;

	public RecordingLoopHarness(CancellationToken cancellationToken, Func<string, Stream>? createFile = null, RecorderType recorderType = RecorderType.BuiltIn)
	{
		_lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		AppConfig config = AppConfig.CreateDefault();
		config.RecordDirectory = _directory.Root;
		config.RecorderType = recorderType;

		if (recorderType is RecorderType.FFmpeg)
		{
			config.FFmpegPath = CreateFakeFFmpeg();
		}

		FakeConfigManager configManager = new(config);

		BilibiliApiClient apiClient = new(new HttpClient(new Handler(RespondApiAsync)), Time);
		LiveRecorder recorder = new(apiClient, new StreamClientFactory(new Handler(RespondStreamAsync)), new FFmpegProvider(configManager), configManager, Time, NullLogger<LiveRecorder>.Instance)
		{
			CreateFile = createFile ?? LiveRecorder.CreateRecordingFile,
			// 磁盘卡住的用例不必等满默认的排空时限
			DrainTimeout = TimeSpan.FromSeconds(1)
		};

		RoomSessionServices services = new
		(
			apiClient,
			new DanmuClient(apiClient, new BilibiliIdentity(static _ => ValueTask.FromResult(new BilibiliRequestOptions(null, null))), NullLogger<DanmuClient>.Instance),
			recorder,
			new RecordingPostProcessor(new UnusedMediaTools(), NullLogger<RecordingPostProcessor>.Instance),
			new LowDiskSpaceMonitor(new UnknownDiskSpace(), configManager, Notifications, Time, NullLogger<LowDiskSpaceMonitor>.Instance),
			Notifications,
			configManager,
			Time,
			NullLogger<RoomSession>.Instance
		);

		Settings = new RoomConfig { RoomId = RoomId, StreamReconnectDelay = ReconnectDelay };
		Session = new RoomSession(Settings, services, static () => { });
	}

	public FakeTimeProvider Time { get; } = new();

	public RecordingNotifications Notifications { get; } = new();

	public RoomConfig Settings { get; }

	public RoomSession Session { get; }

	public RoomSnapshot Snapshot => Session.GetSnapshot();

	public int PlayInfoRequests => Volatile.Read(ref _playInfoRequests);

	public void GoLive()
	{
		Session.Apply(new LiveRoomInfo(RoomId, 0, 1, "streamer", "title", LiveStatus.Live));
	}

	// 之后的状态查询都会返回已下播
	public void GoOffline()
	{
		_isLive = false;
	}

	public async Task<StreamRequest> NextStreamAsync()
	{
		return await _streams.Reader.ReadAsync(_lifetime.Token).AsTask().WaitAsync(WaitTimeout);
	}

	public async Task WaitUntilAsync(Func<RoomSnapshot, bool> condition)
	{
		using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
		timeout.CancelAfter(WaitTimeout);

		while (!condition(Snapshot))
		{
			await Task.Delay(10, timeout.Token);
		}
	}

	// 按秒推进时钟直到出现下一次拉流，返回推进了多久；不依赖重连等待何时注册定时器
	public async Task<(StreamRequest Request, TimeSpan Advanced)> AdvanceUntilNextStreamAsync()
	{
		TimeSpan advanced = TimeSpan.Zero;

		for (int step = 0; step < 60; ++step)
		{
			if (await WaitForStreamAsync(TimeSpan.FromMilliseconds(100)) is { } request)
			{
				return (request, advanced);
			}

			Time.Advance(TimeSpan.FromSeconds(1));
			advanced += TimeSpan.FromSeconds(1);
		}

		throw new TimeoutException("No stream was requested.");
	}

	public async ValueTask DisposeAsync()
	{
		await Session.DisposeAsync();
		await _lifetime.CancelAsync();
		_lifetime.Dispose();
		_directory.Dispose();
	}

	private string CreateFakeFFmpeg()
	{
		if (OperatingSystem.IsWindows())
		{
			string script = _directory.Combine("ffmpeg.cmd");
			File.WriteAllText(script, "@echo ts-data\r\n");
			return script;
		}

		string path = _directory.Combine("ffmpeg");
		File.WriteAllText(path, "#!/bin/sh\necho ts-data\n");
		File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
		return path;
	}

	private async Task<StreamRequest?> WaitForStreamAsync(TimeSpan timeout)
	{
		using CancellationTokenSource wait = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
		wait.CancelAfter(timeout);

		try
		{
			return await _streams.Reader.ReadAsync(wait.Token);
		}
		catch (OperationCanceledException) when (wait.IsCancellationRequested)
		{
			return null;
		}
	}

	private Task<HttpResponseMessage> RespondApiAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		string template = request.RequestUri?.AbsolutePath switch
		{
			"/xlive/web-room/v2/index/getRoomPlayInfo" => PlayInfoTemplate.Replace("$SIGN", Interlocked.Increment(ref _playInfoRequests).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal),
			"/xlive/web-room/v1/index/getRoomBaseInfo" => RoomInfoTemplate,
			var path => throw new InvalidOperationException($"Unexpected API request {path}.")
		};
		string json = template.Replace("$ROOM", RoomId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal).Replace("$LIVE", _isLive ? "1" : "0", StringComparison.Ordinal);

		return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
	}

	private async Task<HttpResponseMessage> RespondStreamAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
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

	private sealed class UnusedMediaTools : IMediaToolAppService
	{
		public Task<string?> GetFFmpegVersionAsync(CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task QueueRemuxAsync(RemuxInput input, CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task QueueCutAsync(CutInput input, CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}
	}
}

// 挂起的拉流请求，由测试决定这个节点如何应答
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

	public void Reject()
	{
		_response.SetResult(new HttpResponseMessage(HttpStatusCode.NotFound));
	}

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

		_response.SetResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new ObservedStream(_body.Reader.AsStream(), OnRead)) });
	}

	public async Task SendAsync()
	{
		await _body.Writer.WriteAsync(new byte[1024]);
	}

	// 一直发送，直到录制端关闭连接
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
