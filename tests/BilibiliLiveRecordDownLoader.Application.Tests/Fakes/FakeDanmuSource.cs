using BilibiliLive.Danmu;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Fakes;

internal sealed class FakeDanmuSource(TimeProvider time)
{
	private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

	private readonly Channel<FakeDanmuConnection> _connections = Channel.CreateUnbounded<FakeDanmuConnection>();

	// 房间的整个生命周期里是同一个对象
	public DanmuReceiveOptions? Options { get; private set; }

	[SuppressMessage("Style", "IDE0060", Justification = "签名要对上 RoomSessionServices.ReceiveDanmu")]
	public async IAsyncEnumerable<DanmuMessage> ReceiveAsync(long roomId, DanmuReceiveOptions options, [EnumeratorCancellation] CancellationToken cancellationToken)
	{
		Options = options;
		FakeDanmuConnection connection = new(time);
		await _connections.Writer.WriteAsync(connection, cancellationToken);

		await foreach (DanmuMessage message in connection.ReadAllAsync(cancellationToken))
		{
			yield return message;
		}
	}

	public Task<FakeDanmuConnection> NextConnectionAsync()
	{
		return _connections.Reader.ReadAsync().AsTask().WaitAsync(WaitTimeout);
	}
}

internal sealed class FakeDanmuConnection(TimeProvider time)
{
	private readonly Channel<DanmuMessage> _messages = Channel.CreateUnbounded<DanmuMessage>();
	private int _sent;
	private int _handled;

	// 接收时刻取当前时钟；房间处理完这条、来取下一条时才返回
	public async Task SendAsync(DanmuMessage message)
	{
		int sent = Interlocked.Increment(ref _sent);
		await _messages.Writer.WriteAsync(message with { ReceivedAt = new DanmuReceiveTime(time.GetTimestamp(), time.GetUtcNow()) });
		await TestWait.UntilAsync(() => Volatile.Read(ref _handled) >= sent, "the room has handled the message", CancellationToken.None);
	}

	public void Close()
	{
		_messages.Writer.TryComplete();
	}

	internal async IAsyncEnumerable<DanmuMessage> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken)
	{
		while (await _messages.Reader.WaitToReadAsync(cancellationToken))
		{
			while (_messages.Reader.TryRead(out DanmuMessage? message))
			{
				yield return message;
				Interlocked.Increment(ref _handled);
			}
		}
	}
}
