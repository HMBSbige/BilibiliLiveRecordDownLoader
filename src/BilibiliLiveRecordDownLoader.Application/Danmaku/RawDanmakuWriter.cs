using System.Buffers;
using System.Text.Json;

namespace BilibiliLiveRecordDownLoader.Application.Danmaku;

// 每行 {"t":接收时刻 Unix 毫秒,"cmd":命令,"raw":原始消息}；连接建立和断开用 @ 开头的命令，B 站的命令都是大写字母，不会重名
// 和弹幕文件一样攒一批按时间或条数写出；不是线程安全的，由房间唯一的写入者调用
internal sealed class RawDanmakuWriter : IAsyncDisposable
{
	public const string ConnectedCommand = @"@connected";
	public const string DisconnectedCommand = @"@disconnected";

	private static readonly JsonWriterOptions WriterOptions = new() { SkipValidation = true };

	private readonly FileStream _stream;
	private readonly DanmakuFlushPolicy _policy;
	private readonly TimeProvider _timeProvider;
	private readonly ArrayBufferWriter<byte> _pending = new();
	private readonly Utf8JsonWriter _json;
	private int _pendingCount;
	private long _firstPendingTimestamp;

	private RawDanmakuWriter(FileStream stream, DanmakuFlushPolicy policy, TimeProvider timeProvider)
	{
		_stream = stream;
		_policy = policy;
		_timeProvider = timeProvider;
		_json = new Utf8JsonWriter(_pending, WriterOptions);
	}

	// 同一次录制里重新打开时接着写
	public static RawDanmakuWriter Open(string path, DanmakuFlushPolicy policy, TimeProvider timeProvider)
	{
		return new RawDanmakuWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, 0, FileOptions.Asynchronous), policy, timeProvider);
	}

	public TimeSpan? FlushDueIn
	{
		get
		{
			if (_pendingCount is 0)
			{
				return null;
			}

			TimeSpan remaining = _policy.Interval - _timeProvider.GetElapsedTime(_firstPendingTimestamp);
			return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
		}
	}

	// json 已由协议层校验过，原样写入
	public ValueTask WriteAsync(DateTimeOffset time, string command, ReadOnlySpan<byte> json, CancellationToken cancellationToken)
	{
		BeginLine(time, command);
		_json.WritePropertyName("raw"u8);
		_json.WriteRawValue(json, true);
		return EndLineAsync(cancellationToken);
	}

	// 连接建立时 uid 是连接所用的身份，匿名为 0
	public ValueTask WriteConnectedAsync(DateTimeOffset time, long userId, CancellationToken cancellationToken)
	{
		BeginLine(time, ConnectedCommand);
		_json.WriteStartObject("raw"u8);
		_json.WriteNumber("uid"u8, userId);
		_json.WriteEndObject();
		return EndLineAsync(cancellationToken);
	}

	public ValueTask WriteDisconnectedAsync(DateTimeOffset time, CancellationToken cancellationToken)
	{
		BeginLine(time, DisconnectedCommand);
		_json.WriteNull("raw"u8);
		return EndLineAsync(cancellationToken);
	}

	public async ValueTask FlushIfDueAsync(CancellationToken cancellationToken)
	{
		if (FlushDueIn == TimeSpan.Zero)
		{
			await FlushAsync(cancellationToken);
		}
	}

	public async ValueTask FlushAsync(CancellationToken cancellationToken)
	{
		if (_pendingCount is 0)
		{
			return;
		}

		await _stream.WriteAsync(_pending.WrittenMemory, cancellationToken);
		await _stream.FlushAsync(cancellationToken);
		_pending.ResetWrittenCount();
		_pendingCount = 0;
	}

	public async ValueTask DisposeAsync()
	{
		try
		{
			await FlushAsync(CancellationToken.None);
		}
		finally
		{
			await _json.DisposeAsync();
			await _stream.DisposeAsync();
		}
	}

	private void BeginLine(DateTimeOffset time, string command)
	{
		if (_pendingCount is 0)
		{
			_firstPendingTimestamp = _timeProvider.GetTimestamp();
		}

		_json.Reset(_pending);
		_json.WriteStartObject();
		_json.WriteNumber("t"u8, time.ToUnixTimeMilliseconds());
		_json.WriteString("cmd"u8, command);
	}

	private async ValueTask EndLineAsync(CancellationToken cancellationToken)
	{
		_json.WriteEndObject();
		_json.Flush();
		_pending.Write("\n"u8);
		++_pendingCount;

		if (_pendingCount >= _policy.MaxPending)
		{
			await FlushAsync(cancellationToken);
		}
		else
		{
			await FlushIfDueAsync(cancellationToken);
		}
	}
}
