using Microsoft.Win32.SafeHandles;

namespace BilibiliLiveRecordDownLoader.Application.Danmaku;

internal static class DanmakuOffsetRewriter
{
	private static readonly DanmakuFlushPolicy RewritePolicy = new(TimeSpan.MaxValue, 4096);

	// 别的格式改写后会变成我们的格式，丢掉原文件里不认识的内容
	// 被偏移推到 0 之前的记录写成 0，再改回来时回不到原值
	// 返回写进文件的记录，和改写后读回的相同
	public static async Task<IReadOnlyList<DanmakuRecord>> RewriteAsync(string path, TimeSpan offset, CancellationToken cancellationToken)
	{
		DanmakuFile file = await DanmakuXmlReader.ReadFileAsync(path, cancellationToken);

		if (file.Source is not DanmakuFileSource.Native { Header: var header })
		{
			throw new InvalidDataException(@"Only danmaku files written by this application can be rewritten.");
		}

		List<DanmakuRecord> written = new(file.Records.Count);
		string temporary = $@"{path}.{Guid.CreateVersion7():N}.tmp";

		try
		{
			await using (DanmakuXmlWriter writer = await DanmakuXmlWriter.CreateFileAsync(temporary, header with { Offset = offset }, RewritePolicy, TimeProvider.System, cancellationToken))
			{
				foreach (DanmakuRecord record in file.Records)
				{
					// 写入者会加上新的偏移，这里先去掉旧的
					DanmakuRecord unshifted = record.Shift(-header.Offset);
					await writer.WriteAsync(unshifted, cancellationToken);
					written.Add(AsWritten(unshifted.Shift(offset)));
				}
			}

			// 不先落盘的话，断电时替换可能先于数据写到磁盘，原文件没了，新文件却是残缺的
			using (SafeFileHandle handle = File.OpenHandle(temporary, FileMode.Open, FileAccess.Write))
			{
				RandomAccess.FlushToDisk(handle);
			}

			File.Move(temporary, path, true);
		}
		catch
		{
			File.Delete(temporary);
			throw;
		}

		return written;
	}

	// 写入者把早于 0 的时间写成 0，文件里的时间截到毫秒
	private static DanmakuRecord AsWritten(DanmakuRecord record)
	{
		return record switch
		{
			DanmakuRecord.Gap gap => gap with { Time = ToFileTime(gap.Time), End = ToFileTime(gap.End) },
			DanmakuRecord.Comment or DanmakuRecord.SuperChat or DanmakuRecord.SuperChatDeletion or DanmakuRecord.Gift or DanmakuRecord.Guard or DanmakuRecord.TitleChange or DanmakuRecord.Connection => record with { Time = ToFileTime(record.Time) }
		};

		static TimeSpan ToFileTime(TimeSpan time)
		{
			return time < TimeSpan.Zero ? TimeSpan.Zero : TimeSpan.FromMilliseconds(time.Ticks / TimeSpan.TicksPerMillisecond);
		}
	}
}
