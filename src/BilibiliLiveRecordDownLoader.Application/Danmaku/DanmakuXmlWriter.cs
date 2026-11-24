using System.Buffers;
using System.Diagnostics;
using System.Text;

namespace BilibiliLiveRecordDownLoader.Application.Danmaku;

// 每次刷新连同 </i> 一起写，下次从 </i> 之前接着写，文件在任何一次刷新之后都是完整的 XML
internal sealed class DanmakuXmlWriter : IAsyncDisposable
{
	private const string ClosingTag = "</i>\n";

	private static readonly UTF8Encoding Utf8 = new(false);

	private readonly Stream _stream;
	private readonly TimeSpan _offset;
	private readonly DanmakuFlushPolicy _policy;
	private readonly TimeProvider _timeProvider;
	private readonly StringBuilder _pending = new();
	private readonly Encoder _encoder = Utf8.GetEncoder();

	private long _contentEnd;
	private long _firstPendingTimestamp;
	private bool _disposed;

	public int PendingCount { get; private set; }

	public long WrittenCount { get; private set; }

	private DanmakuXmlWriter(Stream stream, TimeSpan offset, DanmakuFlushPolicy policy, TimeProvider timeProvider)
	{
		_stream = stream;
		_offset = offset;
		_policy = policy;
		_timeProvider = timeProvider;
		_contentEnd = stream.Position;
	}

	// 接管 stream 的所有权
	public static async Task<DanmakuXmlWriter> CreateAsync(Stream stream, DanmakuFileHeader header, DanmakuFlushPolicy policy, TimeProvider timeProvider, CancellationToken cancellationToken)
	{
		if (!stream.CanSeek || !stream.CanWrite)
		{
			throw new ArgumentException(@"The stream must be writable and seekable.", nameof(stream));
		}

		DanmakuXmlWriter writer = new(stream, header.Offset, policy, timeProvider);
		writer.AppendHeader(header);
		await writer.FlushCoreAsync(cancellationToken);
		return writer;
	}

	public static async Task<DanmakuXmlWriter> CreateFileAsync(string path, DanmakuFileHeader header, DanmakuFlushPolicy policy, TimeProvider timeProvider, CancellationToken cancellationToken)
	{
		FileStream stream = new(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read, 0, FileOptions.Asynchronous);

		try
		{
			return await CreateAsync(stream, header, policy, timeProvider, cancellationToken);
		}
		catch
		{
			await stream.DisposeAsync();

			// CreateNew 保证文件是这次建的；没写完文件头的文件打开时不算弹幕文件，还会占住主干
			try
			{
				File.Delete(path);
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
			}

			throw;
		}
	}

	// 调用方空闲时要在到期后调用 FlushIfDueAsync
	public TimeSpan? FlushDueIn => PendingCount is 0 ? null : _policy.GetFlushDueIn(_timeProvider.GetElapsedTime(_firstPendingTimestamp));

	public async ValueTask WriteAsync(DanmakuRecord record, CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		if (PendingCount is 0)
		{
			_firstPendingTimestamp = _timeProvider.GetTimestamp();
		}

		AppendRecord(record);
		++PendingCount;

		if (PendingCount >= _policy.MaxPending)
		{
			await FlushAsync(cancellationToken);
		}
		else
		{
			await FlushIfDueAsync(cancellationToken);
		}
	}

	public async ValueTask FlushIfDueAsync(CancellationToken cancellationToken)
	{
		if (FlushDueIn == TimeSpan.Zero)
		{
			await FlushAsync(cancellationToken);
		}
	}

	private async ValueTask FlushAsync(CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		if (PendingCount is 0)
		{
			return;
		}

		int count = PendingCount;
		await FlushCoreAsync(cancellationToken);
		WrittenCount += count;
		PendingCount = 0;
	}

	public async ValueTask DisposeAsync()
	{
		if (_disposed)
		{
			return;
		}

		try
		{
			await FlushAsync(CancellationToken.None);
		}
		finally
		{
			_disposed = true;
			await _stream.DisposeAsync();
		}
	}

	private async ValueTask FlushCoreAsync(CancellationToken cancellationToken)
	{
		// 写入失败时保留待写内容，下次从同一位置重写
		byte[] content = ArrayPool<byte>.Shared.Rent(Utf8.GetMaxByteCount(_pending.Length + ClosingTag.Length));

		try
		{
			int length = EncodePending(content);
			_stream.Position = _contentEnd;
			await _stream.WriteAsync(content.AsMemory(0, length), cancellationToken);
			await _stream.FlushAsync(cancellationToken);
			// 新内容总比原来的 </i> 长，文件只会变长，不需要截断
			_contentEnd += length - ClosingTag.Length;
			_pending.Clear();
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(content);
		}
	}

	// 代理对可能跨在两个块之间，要用同一个编码器接着编码
	private int EncodePending(Span<byte> destination)
	{
		int length = 0;

		foreach (ReadOnlyMemory<char> chunk in _pending.GetChunks())
		{
			length += _encoder.GetBytes(chunk.Span, destination.Slice(length), false);
		}

		return length + _encoder.GetBytes(ClosingTag, destination.Slice(length), true);
	}

	private void AppendHeader(DanmakuFileHeader header)
	{
		_pending.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<i>\n");
		// B 站视频弹幕文件的固定头，有的播放器和转换器靠它识别格式
		_pending.Append("  <chatserver>chat.bilibili.com</chatserver><chatid>0</chatid><mission>0</mission><maxlimit>0</maxlimit><state>0</state><real_name>0</real_name><source>k-v</source>\n");
		_pending.Append(@"  <BilibiliLiveRecordDownLoader");
		AppendAttribute(@"version", header.Version);
		AppendAttribute(@"room_id", header.RoomId);
		AppendAttribute(@"short_id", header.ShortId);
		AppendAttribute(@"streamer", header.Streamer);
		AppendAttribute(@"title", header.Title);
		AppendAttribute(@"area", header.Area);
		AppendAttribute(@"parent_area", header.ParentArea);
		AppendAttribute(@"live_id", header.LiveId);
		AppendAttribute(@"live_start_time", DanmakuXmlText.FormatTimestamp(header.LiveStartTime));
		AppendAttribute(@"record_start_time", DanmakuXmlText.FormatTimestamp(header.RecordStartTime));
		_pending.Append(@" offset=""");
		DanmakuXmlText.AppendSeconds(_pending, header.Offset);
		_pending.Append('"');
		_pending.Append(" />\n");
	}

	private void AppendRecord(DanmakuRecord record)
	{
		_pending.Append(@"  ");

		switch (record)
		{
			case DanmakuRecord.Comment comment:
			{
				_pending.Append(@"<d p=""");
				AppendTime(comment.Time);
				_pending.Append(CultureInfo.InvariantCulture, $",{comment.Mode},{comment.FontSize},{comment.Color},{comment.SentAt.ToUnixTimeSeconds()},0,");
				AppendField(comment.UidHash);
				_pending.Append(CultureInfo.InvariantCulture, $",{comment.DmId}\"");
				AppendUser(comment.Uid, comment.User);

				if (comment.IsStorm)
				{
					AppendAttribute(@"storm", @"1");
				}

				_pending.Append('>');
				DanmakuXmlText.AppendEscaped(_pending, comment.Text);
				_pending.Append(@"</d>");
				break;
			}
			case DanmakuRecord.SuperChat superChat:
			{
				_pending.Append(@"<sc");
				AppendTimeAttribute(@"ts", superChat.Time);
				AppendUser(superChat.Uid, superChat.User);
				// 和 blrec 一样用千分之一元：录播姬写的是元，DanmakuFactory 只对录播姬的文件乘 1000，其他文件按千分之一元读
				AppendAttribute(@"price", superChat.Price);
				AppendAttribute(@"time", (long)superChat.Duration.TotalSeconds);
				AppendAttribute(@"id", superChat.Id);
				_pending.Append('>');
				DanmakuXmlText.AppendEscaped(_pending, superChat.Text);
				_pending.Append(@"</sc>");
				break;
			}
			case DanmakuRecord.SuperChatDeletion deletion:
			{
				_pending.Append(@"<sc_delete");
				AppendTimeAttribute(@"ts", deletion.Time);
				_pending.Append(@" ids=""");

				for (int i = 0; i < deletion.Ids.Count; ++i)
				{
					_pending.Append(CultureInfo.InvariantCulture, $"{(i > 0 ? "," : string.Empty)}{deletion.Ids[i]}");
				}

				_pending.Append(@""" />");
				break;
			}
			case DanmakuRecord.Gift gift:
			{
				_pending.Append(@"<gift");
				AppendTimeAttribute(@"ts", gift.Time);
				AppendUser(gift.Uid, gift.User);
				AppendAttribute(@"giftname", gift.Name);
				AppendAttribute(@"giftcount", gift.Count);
				AppendAttribute(@"price", gift.Price);
				_pending.Append(@" />");
				break;
			}
			case DanmakuRecord.Guard guard:
			{
				_pending.Append(@"<guard");
				AppendTimeAttribute(@"ts", guard.Time);
				AppendUser(guard.Uid, guard.User);
				AppendAttribute(@"level", (int)guard.Level);
				AppendAttribute(@"count", guard.Count);
				AppendAttribute(@"price", guard.Price);

				if (guard.IsRenewal)
				{
					AppendAttribute(@"renewal", @"1");
				}

				_pending.Append(@" />");
				break;
			}
			case DanmakuRecord.TitleChange title:
			{
				_pending.Append(@"<title");
				AppendTimeAttribute(@"ts", title.Time);
				_pending.Append('>');
				DanmakuXmlText.AppendEscaped(_pending, title.Title);
				_pending.Append(@"</title>");
				break;
			}
			case DanmakuRecord.Gap gap:
			{
				_pending.Append(@"<gap");
				AppendTimeAttribute(@"start", gap.Time);
				AppendTimeAttribute(@"end", gap.End);
				_pending.Append(@" />");
				break;
			}
			case DanmakuRecord.Connection connection:
			{
				_pending.Append(@"<connection");
				AppendTimeAttribute(@"ts", connection.Time);
				AppendAttribute(@"anonymous", connection.Anonymous ? @"true" : @"false");
				AppendAttribute(@"masked", connection.Masked ? @"true" : @"false");
				_pending.Append(@" />");
				break;
			}
			default:
			{
				throw new UnreachableException();
			}
		}

		_pending.Append('\n');
	}

	// p 里的字段用逗号分隔，字段本身不能带逗号；空值按 B 站的习惯写 0
	private void AppendField(string value)
	{
		string field = value.Replace(",", string.Empty, StringComparison.Ordinal);
		DanmakuXmlText.AppendEscaped(_pending, field.Length is 0 ? @"0" : field);
	}

	private void AppendUser(long uid, string user)
	{
		AppendAttribute(@"uid", uid);
		AppendAttribute(@"user", user);
	}

	private void AppendAttribute(string name, string value)
	{
		_pending.Append(' ').Append(name).Append(@"=""");
		DanmakuXmlText.AppendEscaped(_pending, value);
		_pending.Append('"');
	}

	private void AppendAttribute(string name, long value)
	{
		_pending.Append(CultureInfo.InvariantCulture, $" {name}=\"{value}\"");
	}

	private void AppendTimeAttribute(string name, TimeSpan value)
	{
		_pending.Append(' ').Append(name).Append(@"=""");
		AppendTime(value);
		_pending.Append('"');
	}

	// 偏移可能把开头的记录推到 0 之前
	private void AppendTime(TimeSpan value)
	{
		TimeSpan shifted = value + _offset;
		DanmakuXmlText.AppendSeconds(_pending, shifted < TimeSpan.Zero ? TimeSpan.Zero : shifted);
	}
}
