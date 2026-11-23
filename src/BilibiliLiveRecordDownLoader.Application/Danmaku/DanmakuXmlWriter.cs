using System.Diagnostics;
using System.Text;

namespace BilibiliLiveRecordDownLoader.Application.Danmaku;

// 每次刷新连同 </i> 一起写，下次从 </i> 之前接着写，文件在任何一次刷新之后都是完整的 XML
// 不是线程安全的
internal sealed class DanmakuXmlWriter : IAsyncDisposable
{
	private const string ClosingTag = "</i>\n";

	private static readonly UTF8Encoding Utf8 = new(false);

	private readonly Stream _stream;
	private readonly TimeSpan _offset;
	private readonly DanmakuFlushPolicy _policy;
	private readonly TimeProvider _timeProvider;
	private readonly StringBuilder _pending = new();

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

	// 主干是否可用由调用方先判断
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
				// 删不掉也要报出原来的错误
			}

			throw;
		}
	}

	// 没有待写内容时为 null；调用方空闲时要在到期后调用 FlushIfDueAsync
	public TimeSpan? FlushDueIn => PendingCount is 0 ? null : _policy.GetFlushDueIn(_timeProvider.GetElapsedTime(_firstPendingTimestamp));

	public async ValueTask WriteAsync(DanmakuRecord record, CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		if (PendingCount is 0)
		{
			_firstPendingTimestamp = _timeProvider.GetTimestamp();
		}

		AppendRecord(record.Shift(_offset));
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
		byte[] content = Utf8.GetBytes(_pending + ClosingTag);
		_stream.Position = _contentEnd;
		await _stream.WriteAsync(content, cancellationToken);
		await _stream.FlushAsync(cancellationToken);
		// 新内容总比原来的 </i> 长，文件只会变长，不需要截断
		_contentEnd += content.Length - ClosingTag.Length;
		_pending.Clear();
	}

	private void AppendHeader(DanmakuFileHeader header)
	{
		_pending.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<i>\n");
		// B 站视频弹幕文件的固定头，有的播放器和转换器靠它识别格式
		_pending.Append("  <chatserver>chat.bilibili.com</chatserver><chatid>0</chatid><mission>0</mission><maxlimit>0</maxlimit><state>0</state><real_name>0</real_name><source>k-v</source>\n");
		_pending.Append(@"  <BilibiliLiveRecordDownLoader");
		AppendAttribute(@"version", header.Version);
		AppendAttribute(@"room_id", DanmakuXmlText.Format(header.RoomId));
		AppendAttribute(@"short_id", DanmakuXmlText.Format(header.ShortId));
		AppendAttribute(@"streamer", header.Streamer);
		AppendAttribute(@"title", header.Title);
		AppendAttribute(@"area", header.Area);
		AppendAttribute(@"parent_area", header.ParentArea);
		AppendAttribute(@"live_id", header.LiveId);
		AppendAttribute(@"live_start_time", DanmakuXmlText.FormatTimestamp(header.LiveStartTime));
		AppendAttribute(@"record_start_time", DanmakuXmlText.FormatTimestamp(header.RecordStartTime));
		AppendAttribute(@"offset", DanmakuXmlText.FormatSeconds(header.Offset));
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
				_pending.Append(FormatTime(comment.Time)).Append(',');
				_pending.Append(DanmakuXmlText.Format(comment.Mode)).Append(',');
				_pending.Append(DanmakuXmlText.Format(comment.FontSize)).Append(',');
				_pending.Append(DanmakuXmlText.Format(comment.Color)).Append(',');
				_pending.Append(DanmakuXmlText.Format(comment.SentAt.ToUnixTimeSeconds())).Append(',');
				_pending.Append(@"0,");
				AppendField(comment.UidHash);
				_pending.Append(',');
				_pending.Append(DanmakuXmlText.Format(comment.DmId));
				_pending.Append('"');
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
				AppendAttribute(@"ts", FormatTime(superChat.Time));
				AppendUser(superChat.Uid, superChat.User);
				// 和 blrec 一样用千分之一元：录播姬写的是元，DanmakuFactory 只对录播姬的文件乘 1000，其他文件按千分之一元读
				AppendAttribute(@"price", DanmakuXmlText.Format(superChat.Price));
				AppendAttribute(@"time", DanmakuXmlText.Format((long)superChat.Duration.TotalSeconds));
				AppendAttribute(@"id", DanmakuXmlText.Format(superChat.Id));
				_pending.Append('>');
				DanmakuXmlText.AppendEscaped(_pending, superChat.Text);
				_pending.Append(@"</sc>");
				break;
			}
			case DanmakuRecord.SuperChatDeletion deletion:
			{
				_pending.Append(@"<sc_delete");
				AppendAttribute(@"ts", FormatTime(deletion.Time));
				AppendAttribute(@"ids", string.Join(',', deletion.Ids.Select(DanmakuXmlText.Format)));
				_pending.Append(@" />");
				break;
			}
			case DanmakuRecord.Gift gift:
			{
				_pending.Append(@"<gift");
				AppendAttribute(@"ts", FormatTime(gift.Time));
				AppendUser(gift.Uid, gift.User);
				AppendAttribute(@"giftname", gift.Name);
				AppendAttribute(@"giftcount", DanmakuXmlText.Format(gift.Count));
				AppendAttribute(@"price", DanmakuXmlText.Format(gift.Price));
				_pending.Append(@" />");
				break;
			}
			case DanmakuRecord.Guard guard:
			{
				_pending.Append(@"<guard");
				AppendAttribute(@"ts", FormatTime(guard.Time));
				AppendUser(guard.Uid, guard.User);
				AppendAttribute(@"level", DanmakuXmlText.Format((int)guard.Level));
				AppendAttribute(@"count", DanmakuXmlText.Format(guard.Count));
				AppendAttribute(@"price", DanmakuXmlText.Format(guard.Price));

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
				AppendAttribute(@"ts", FormatTime(title.Time));
				_pending.Append('>');
				DanmakuXmlText.AppendEscaped(_pending, title.Title);
				_pending.Append(@"</title>");
				break;
			}
			case DanmakuRecord.Gap gap:
			{
				_pending.Append(@"<gap");
				AppendAttribute(@"start", FormatTime(gap.Time));
				AppendAttribute(@"end", FormatTime(gap.End));
				_pending.Append(@" />");
				break;
			}
			case DanmakuRecord.Connection connection:
			{
				_pending.Append(@"<connection");
				AppendAttribute(@"ts", FormatTime(connection.Time));
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
		AppendAttribute(@"uid", DanmakuXmlText.Format(uid));
		AppendAttribute(@"user", user);
	}

	private void AppendAttribute(string name, string value)
	{
		_pending.Append(' ').Append(name).Append(@"=""");
		DanmakuXmlText.AppendEscaped(_pending, value);
		_pending.Append('"');
	}

	// 偏移可能把开头的记录推到 0 之前
	private static string FormatTime(TimeSpan value)
	{
		return DanmakuXmlText.FormatSeconds(value < TimeSpan.Zero ? TimeSpan.Zero : value);
	}
}
