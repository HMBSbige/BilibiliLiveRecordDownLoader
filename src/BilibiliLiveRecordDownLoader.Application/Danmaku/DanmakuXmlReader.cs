using System.Xml;

namespace BilibiliLiveRecordDownLoader.Application.Danmaku;

// 读自己的文件，其他文件一律按 B 站视频弹幕的格式只读 d，别的扩展元素各家含义和单位不同，跳过；文件可能正在录制或已被截断，读到出错为止
internal static class DanmakuXmlReader
{
	private static readonly XmlReaderSettings Settings = new()
	{
		Async = true,
		// 别的程序写的文件可能带非法字符，宁可读进来也不要整份放弃
		CheckCharacters = false,
		DtdProcessing = DtdProcessing.Ignore,
		XmlResolver = null,
		IgnoreComments = true,
		IgnoreProcessingInstructions = true,
		IgnoreWhitespace = true
	};

	public static async Task<DanmakuFile> ReadFileAsync(string path, CancellationToken cancellationToken)
	{
		// 正在录制的文件被写入者以 FileShare.Read 打开，这里必须允许写共享
		await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
		return await ReadAsync(stream, cancellationToken);
	}

	public static async Task<DanmakuFile> ReadAsync(Stream stream, CancellationToken cancellationToken)
	{
		if (!stream.CanSeek)
		{
			throw new ArgumentException(@"The stream must be seekable.", nameof(stream));
		}

		DanmakuFileSource source = new DanmakuFileSource.Standard();
		List<DanmakuRecord> records = [];
		bool complete = true;

		try
		{
			using XmlReader reader = XmlReader.Create(stream, Settings);
			bool positioned = false;

			while (positioned || await reader.ReadAsync())
			{
				cancellationToken.ThrowIfCancellationRequested();
				positioned = false;

				// 记录都是根元素的直接子元素；有的文件内嵌了 XSL，里面的 HTML 元素也叫 title 之类，不能当成记录
				if (reader is not { NodeType: XmlNodeType.Element, Depth: 1 })
				{
					continue;
				}

				// 自己的文件头写在所有记录之前
				bool isNative = source is DanmakuFileSource.Native;

				switch (reader.LocalName)
				{
					case @"BilibiliLiveRecordDownLoader":
					{
						source = new DanmakuFileSource.Native(ReadHeader(reader));
						break;
					}
					case @"d":
					{
						// 先解析属性，认不出的元素不读内容，内容里有子元素时读内容会抛异常
						if (ParseComment(reader.GetAttribute(@"p"), reader.GetAttribute(@"uid"), reader.GetAttribute(@"user"), reader.GetAttribute(@"storm") is @"1") is { } comment)
						{
							records.Add(comment with { Text = await reader.ReadElementContentAsStringAsync() });
							positioned = true;
						}

						break;
					}
					case @"sc" when isNative:
					{
						if (DanmakuXmlText.TryParseSeconds(reader.GetAttribute(@"ts"), out TimeSpan time))
						{
							long uid = DanmakuXmlText.ParseInt64(reader.GetAttribute(@"uid"));
							string user = reader.GetAttribute(@"user") ?? string.Empty;
							long price = DanmakuXmlText.ParseInt64(reader.GetAttribute(@"price"));
							TimeSpan duration = DanmakuXmlText.TryParseSeconds(reader.GetAttribute(@"time"), out TimeSpan value) ? value : TimeSpan.Zero;
							long id = DanmakuXmlText.ParseInt64(reader.GetAttribute(@"id"));
							records.Add(new DanmakuRecord.SuperChat(time, uid, user, price, duration, await reader.ReadElementContentAsStringAsync()) { Id = id });
							positioned = true;
						}

						break;
					}
					case @"sc_delete" when isNative:
					{
						if (DanmakuXmlText.TryParseSeconds(reader.GetAttribute(@"ts"), out TimeSpan time) && ParseIds(reader.GetAttribute(@"ids")) is { Length: > 0 } ids)
						{
							records.Add(new DanmakuRecord.SuperChatDeletion(time, ids));
						}

						break;
					}
					case @"gift" when isNative:
					{
						if (DanmakuXmlText.TryParseSeconds(reader.GetAttribute(@"ts"), out TimeSpan time))
						{
							records.Add(new DanmakuRecord.Gift(
								time,
								DanmakuXmlText.ParseInt64(reader.GetAttribute(@"uid")),
								reader.GetAttribute(@"user") ?? string.Empty,
								reader.GetAttribute(@"giftname") ?? string.Empty,
								DanmakuXmlText.ParseInt32(reader.GetAttribute(@"giftcount") ?? reader.GetAttribute(@"count"), 1),
								DanmakuXmlText.ParseInt64(reader.GetAttribute(@"price"))));
						}

						break;
					}
					case @"guard" when isNative:
					{
						if (DanmakuXmlText.TryParseSeconds(reader.GetAttribute(@"ts"), out TimeSpan time))
						{
							int level = DanmakuXmlText.ParseInt32(reader.GetAttribute(@"level"));
							int count = DanmakuXmlText.ParseInt32(reader.GetAttribute(@"count"), 1);
							bool isRenewal = reader.GetAttribute(@"renewal") is @"1";
							records.Add(new DanmakuRecord.Guard(time, DanmakuXmlText.ParseInt64(reader.GetAttribute(@"uid")), reader.GetAttribute(@"user") ?? string.Empty, level, count, DanmakuXmlText.ParseInt64(reader.GetAttribute(@"price"))) { IsRenewal = isRenewal });
						}

						break;
					}
					case @"title" when isNative:
					{
						if (DanmakuXmlText.TryParseSeconds(reader.GetAttribute(@"ts"), out TimeSpan time))
						{
							records.Add(new DanmakuRecord.TitleChange(time, await reader.ReadElementContentAsStringAsync()));
							positioned = true;
						}

						break;
					}
					case @"gap" when isNative:
					{
						if (DanmakuXmlText.TryParseSeconds(reader.GetAttribute(@"start"), out TimeSpan start) && DanmakuXmlText.TryParseSeconds(reader.GetAttribute(@"end"), out TimeSpan end))
						{
							records.Add(new DanmakuRecord.Gap(start, end));
						}

						break;
					}
					case @"connection" when isNative:
					{
						if (DanmakuXmlText.TryParseSeconds(reader.GetAttribute(@"ts"), out TimeSpan time))
						{
							records.Add(new DanmakuRecord.Connection(time, reader.GetAttribute(@"anonymous") is @"true", reader.GetAttribute(@"masked") is @"true"));
						}

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

	// p[4] 是 Unix 秒，p[6] 是 uid 的哈希；uid 和 user 属性只有自己的文件有
	private static DanmakuRecord.Comment? ParseComment(string? p, string? uidAttribute, string? user, bool storm)
	{
		if (p is null)
		{
			return null;
		}

		string[] fields = p.Split(',');

		if (!DanmakuXmlText.TryParseSeconds(fields[0], out TimeSpan time))
		{
			return null;
		}

		int mode = DanmakuXmlText.ParseInt32(Field(fields, 1), 1);
		int fontSize = DanmakuXmlText.ParseInt32(Field(fields, 2), 25);
		int color = (int)(DanmakuXmlText.ParseInt64(Field(fields, 3), 0xFFFFFF) & 0xFFFFFF);
		long sent = DanmakuXmlText.ParseInt64(Field(fields, 4));
		DateTimeOffset sentAt = FromUnixSeconds(sent);
		string sender = Field(fields, 6) ?? @"0";
		string dmId = Field(fields, 7) is { Length: > 0 } id ? id : @"0";
		long uid = uidAttribute is not null ? DanmakuXmlText.ParseInt64(uidAttribute) : 0;

		// 打码时 uid 为 0 而哈希仍是真的，原样保留
		return new DanmakuRecord.Comment(time, mode, fontSize, color, sentAt, uid, user ?? string.Empty, string.Empty, dmId) { IsStorm = storm, UidHash = sender };
	}

	// 0 是缺失的 id，留着会删掉旧文件里所有没有 id 的 SC
	private static long[] ParseIds(string? value)
	{
		return value is null ? [] : [.. value.Split(',').Select(static id => DanmakuXmlText.ParseInt64(id)).Where(static id => id > 0)];
	}

	private static string? Field(string[] fields, int index)
	{
		return index < fields.Length ? fields[index] : null;
	}

	private static DateTimeOffset FromUnixSeconds(long value)
	{
		return value is >= 0 and <= 253402300799 ? DateTimeOffset.FromUnixTimeSeconds(value) : DateTimeOffset.UnixEpoch;
	}
}
