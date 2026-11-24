using System.Xml;

namespace BilibiliLiveRecordDownLoader.Application.Danmaku;

// 别人的文件只读 d，各家扩展元素的含义和单位不同；文件可能正在录制或已被截断，读到出错为止
internal static class DanmakuXmlReader
{
	private static readonly XmlReaderSettings Settings = new()
	{
		Async = true,
		// 别的程序写的文件可能带非法字符
		CheckCharacters = false,
		DtdProcessing = DtdProcessing.Ignore,
		XmlResolver = null,
		IgnoreComments = true,
		IgnoreProcessingInstructions = true,
		IgnoreWhitespace = true
	};

	// 按时间分格统计时数组随最后一条的时间增长，要挡掉坏数据；B 站视频不会超过一天
	// 仅弹幕录制不按时长分段，自己文件的上限不能碰到真实的录制
	private static readonly TimeSpan MaxStandardTime = TimeSpan.FromDays(1);
	private static readonly TimeSpan MaxNativeTime = TimeSpan.FromDays(366);

	public static async Task<DanmakuFile> ReadFileAsync(string path, CancellationToken cancellationToken)
	{
		// 正在录制的文件被写入者以 FileShare.Read 打开，这里必须允许写共享
		await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
		return await ReadAsync(stream, cancellationToken);
	}

	public static async Task<DanmakuFile> ReadAsync(Stream stream, CancellationToken cancellationToken)
	{
		DanmakuFileSource source = new DanmakuFileSource.Standard();
		List<DanmakuRecord> records = [];
		bool complete = true;

		try
		{
			using XmlReader reader = XmlReader.Create(stream, Settings);

			while (await reader.ReadAsync())
			{
				cancellationToken.ThrowIfCancellationRequested();

				// 有的文件内嵌了 XSL，里面的 HTML 元素也叫 title 之类
				if (reader is not { NodeType: XmlNodeType.Element, Depth: 1 })
				{
					continue;
				}

				bool isNative = source is DanmakuFileSource.Native;

				switch (reader.LocalName)
				{
					case @"BilibiliLiveRecordDownLoader":
					{
						source = new DanmakuFileSource.Native(ReadHeader(reader));
						break;
					}
					case @"d" when await ReadCommentAsync(reader, isNative) is { } comment:
					{
						records.Add(comment);
						break;
					}
					case @"sc" when isNative && TryReadNativeTime(reader, @"ts", out TimeSpan time):
					{
						long uid = DanmakuXmlText.ParseInt64(reader.GetAttribute(@"uid"));
						string user = reader.GetAttribute(@"user") ?? string.Empty;
						long price = DanmakuXmlText.ParseInt64(reader.GetAttribute(@"price"));
						TimeSpan duration = DanmakuXmlText.TryParseSeconds(reader.GetAttribute(@"time"), out TimeSpan value) ? value : TimeSpan.Zero;
						long id = DanmakuXmlText.ParseInt64(reader.GetAttribute(@"id"));
						records.Add(new DanmakuRecord.SuperChat(time, uid, user, price, duration, await ReadTextAsync(reader)) { Id = id });
						break;
					}
					case @"sc_delete" when isNative && TryReadNativeTime(reader, @"ts", out TimeSpan time) && ParseIds(reader.GetAttribute(@"ids")) is { Count: > 0 } ids:
					{
						records.Add(new DanmakuRecord.SuperChatDeletion(time, ids));
						break;
					}
					case @"gift" when isNative && TryReadNativeTime(reader, @"ts", out TimeSpan time):
					{
						records.Add(new DanmakuRecord.Gift(
							time,
							DanmakuXmlText.ParseInt64(reader.GetAttribute(@"uid")),
							reader.GetAttribute(@"user") ?? string.Empty,
							reader.GetAttribute(@"giftname") ?? string.Empty,
							DanmakuXmlText.ParseInt32(reader.GetAttribute(@"giftcount"), 1),
							DanmakuXmlText.ParseInt64(reader.GetAttribute(@"price"))));
						break;
					}
					case @"guard" when isNative && TryReadNativeTime(reader, @"ts", out TimeSpan time):
					{
						GuardLevel level = (GuardLevel)DanmakuXmlText.ParseInt32(reader.GetAttribute(@"level"));
						int count = DanmakuXmlText.ParseInt32(reader.GetAttribute(@"count"), 1);
						bool isRenewal = reader.GetAttribute(@"renewal") is @"1";
						records.Add(new DanmakuRecord.Guard(time, DanmakuXmlText.ParseInt64(reader.GetAttribute(@"uid")), reader.GetAttribute(@"user") ?? string.Empty, Enum.IsDefined(level) ? level : GuardLevel.None, count, DanmakuXmlText.ParseInt64(reader.GetAttribute(@"price"))) { IsRenewal = isRenewal });
						break;
					}
					case @"title" when isNative && TryReadNativeTime(reader, @"ts", out TimeSpan time):
					{
						records.Add(new DanmakuRecord.TitleChange(time, await ReadTextAsync(reader)));
						break;
					}
					case @"gap" when isNative && TryReadNativeTime(reader, @"start", out TimeSpan start) && TryReadNativeTime(reader, @"end", out TimeSpan end):
					{
						records.Add(new DanmakuRecord.Gap(start, end));
						break;
					}
					case @"connection" when isNative && TryReadNativeTime(reader, @"ts", out TimeSpan time):
					{
						records.Add(new DanmakuRecord.Connection(time, reader.GetAttribute(@"anonymous") is @"true", reader.GetAttribute(@"masked") is @"true"));
						break;
					}
				}
			}
		}
		catch (XmlException)
		{
			complete = false;
		}

		return new DanmakuFile(source, records, complete);
	}

	// 不越过结束标签：越过时要解析下一个节点，文件正好截断在这条之后时会在那里出错，丢掉已经完整的这一条
	private static async ValueTask<string> ReadTextAsync(XmlReader reader)
	{
		if (reader.IsEmptyElement)
		{
			return string.Empty;
		}

		await reader.ReadAsync();
		return reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace ? await reader.ReadContentAsStringAsync() : string.Empty;
	}

	private static bool TryReadNativeTime(XmlReader reader, string attribute, out TimeSpan time)
	{
		return TryParseTime(reader.GetAttribute(attribute), MaxNativeTime, out time);
	}

	// 写入者把记录的时间截到 0，负值只会来自坏数据
	private static bool TryParseTime(ReadOnlySpan<char> value, TimeSpan max, out TimeSpan time)
	{
		return DanmakuXmlText.TryParseSeconds(value, out time) && time >= TimeSpan.Zero && time <= max;
	}

	private static DanmakuFileHeader ReadHeader(XmlReader reader)
	{
		return new DanmakuFileHeader
		{
			Version = reader.GetAttribute(@"version") ?? string.Empty,
			RoomId = DanmakuXmlText.ParseInt64(reader.GetAttribute(@"room_id")),
			ShortId = DanmakuXmlText.ParseInt64(reader.GetAttribute(@"short_id")),
			Streamer = reader.GetAttribute(@"streamer") ?? string.Empty,
			Title = reader.GetAttribute(@"title") ?? string.Empty,
			Area = reader.GetAttribute(@"area") ?? string.Empty,
			ParentArea = reader.GetAttribute(@"parent_area") ?? string.Empty,
			LiveId = reader.GetAttribute(@"live_id") ?? string.Empty,
			LiveStartTime = DanmakuXmlText.TryParseTimestamp(reader.GetAttribute(@"live_start_time"), out DateTimeOffset liveStart) ? liveStart : null,
			RecordStartTime = DanmakuXmlText.TryParseTimestamp(reader.GetAttribute(@"record_start_time"), out DateTimeOffset recordStart) ? recordStart : null,
			Offset = DanmakuXmlText.TryParseSeconds(reader.GetAttribute(@"offset"), out TimeSpan offset) ? offset : TimeSpan.Zero
		};
	}

	// p[4] 是 Unix 秒，p[6] 是 uid 的哈希
	// B 站标准的 d 只有 p，uid、user 和 storm 属性只读自己的文件：别的程序写在 d 上的同名属性含义不一定相同
	private static async ValueTask<DanmakuRecord.Comment?> ReadCommentAsync(XmlReader reader, bool isNative)
	{
		string? p = reader.GetAttribute(@"p");

		if (p is null)
		{
			return null;
		}

		Span<Range> ranges = stackalloc Range[9];
		ReadOnlySpan<Range> fields = ranges.Slice(0, p.AsSpan().Split(ranges, ','));

		if (!TryParseTime(p.AsSpan(fields[0]), isNative ? MaxNativeTime : MaxStandardTime, out TimeSpan time))
		{
			return null;
		}

		int mode = DanmakuXmlText.ParseInt32(GetField(p, fields, 1), 1);
		int fontSize = DanmakuXmlText.ParseInt32(GetField(p, fields, 2), 25);
		int color = (int)(DanmakuXmlText.ParseInt64(GetField(p, fields, 3), 0xFFFFFF) & 0xFFFFFF);
		DateTimeOffset sentAt = FromUnixSeconds(DanmakuXmlText.ParseInt64(GetField(p, fields, 4)));
		long dmId = DanmakuXmlText.ParseInt64(GetField(p, fields, 7));
		string uidHash = fields.Length > 6 ? p[fields[6]] : @"0";
		long uid = isNative ? DanmakuXmlText.ParseInt64(reader.GetAttribute(@"uid")) : 0;
		string user = isNative ? reader.GetAttribute(@"user") ?? string.Empty : string.Empty;
		bool storm = isNative && reader.GetAttribute(@"storm") is @"1";

		return new DanmakuRecord.Comment(time, mode, fontSize, color, sentAt, uid, user, await ReadTextAsync(reader), dmId, uidHash) { IsStorm = storm };
	}

	private static ReadOnlySpan<char> GetField(string p, ReadOnlySpan<Range> fields, int index)
	{
		return index < fields.Length ? p.AsSpan(fields[index]) : default;
	}

	// 0 是缺失的 id，留着会删掉所有缺少 id 的 SC
	private static List<long> ParseIds(string? value)
	{
		if (value is null)
		{
			return [];
		}

		List<long> ids = [];

		foreach (Range range in value.AsSpan().Split(','))
		{
			if (DanmakuXmlText.ParseInt64(value.AsSpan(range)) is > 0 and var id)
			{
				ids.Add(id);
			}
		}

		return ids;
	}

	private static DateTimeOffset FromUnixSeconds(long value)
	{
		return value is >= 0 and <= 253402300799 ? DateTimeOffset.FromUnixTimeSeconds(value) : DateTimeOffset.UnixEpoch;
	}
}
