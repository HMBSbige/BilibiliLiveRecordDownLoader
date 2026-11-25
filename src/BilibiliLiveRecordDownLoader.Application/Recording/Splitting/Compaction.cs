namespace BilibiliLiveRecordDownLoader.Application.Recording.Splitting;

// 去填充时在同一个缓冲区里从前往后压紧；destination 不在 source 之后，搬过去的数据不会覆盖还没读的部分
internal static class Compaction
{
	public static int Keep(Span<byte> buffer, int source, int destination, int length)
	{
		if (source != destination)
		{
			buffer.Slice(source, length).CopyTo(buffer.Slice(destination));
		}

		return length;
	}
}
