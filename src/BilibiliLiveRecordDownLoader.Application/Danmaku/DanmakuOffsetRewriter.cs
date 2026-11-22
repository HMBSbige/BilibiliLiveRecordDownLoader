namespace BilibiliLiveRecordDownLoader.Application.Danmaku;

internal static class DanmakuOffsetRewriter
{
	private static readonly DanmakuFlushPolicy RewritePolicy = new(TimeSpan.MaxValue, 4096);

	// 别的格式改写后会变成我们的格式，丢掉原文件里不认识的内容
	// 被偏移推到 0 之前的记录写成 0，再改回来时回不到原值
	public static async Task<DanmakuFileHeader> RewriteAsync(string path, TimeSpan offset, CancellationToken cancellationToken)
	{
		DanmakuFile file = await DanmakuXmlReader.ReadFileAsync(path, cancellationToken);

		if (file.Source is not DanmakuFileSource.Native { Header: var header })
		{
			throw new InvalidDataException(@"Only danmaku files written by this application can be rewritten.");
		}

		DanmakuFileHeader updated = header with { Offset = offset };
		string temporary = $@"{path}.{Guid.CreateVersion7():N}.tmp";

		try
		{
			await using (DanmakuXmlWriter writer = await DanmakuXmlWriter.CreateFileAsync(temporary, updated, RewritePolicy, TimeProvider.System, cancellationToken))
			{
				foreach (DanmakuRecord record in file.Records)
				{
					// 写入者会加上新的偏移，这里先去掉旧的
					await writer.WriteAsync(record.Shift(-header.Offset), cancellationToken);
				}
			}

			// 不先落盘的话，断电时替换可能先于数据写到磁盘，原文件没了，新文件却是残缺的
			await using (FileStream stream = new(temporary, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
			{
				stream.Flush(true);
			}

			File.Move(temporary, path, true);
		}
		catch
		{
			File.Delete(temporary);
			throw;
		}

		return updated;
	}
}
