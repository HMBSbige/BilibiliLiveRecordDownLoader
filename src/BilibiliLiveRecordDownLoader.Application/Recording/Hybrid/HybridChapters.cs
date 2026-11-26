using System.Buffers.Binary;
using System.Text;
using System.Text.Unicode;

namespace BilibiliLiveRecordDownLoader.Application.Recording.Hybrid;

// Duration 按 HybridChapters.Timescale，到下一章开始，最后一章到它自己的结尾
internal readonly record struct ChapterSample(uint Duration, TimeSpan Start, string Title, byte[] Data);

// Start 是第一章的开始，End 是最晚的章节结尾，都按电影时间刻度
internal sealed record ChapterTrack(uint TrackId, ChapterSample[] Samples, long Start, long End);

// 两种章节都写，读取方各认其中一种：QuickTime 章节文本轨（tref chap 指向的文本轨，每章一个文本样本）和 udta 里的 Nero chpl
internal static class HybridChapters
{
	// 章节轨按毫秒计：电影时间刻度是各轨道时间刻度的最小公倍数，90 kHz 视频加 48 kHz 音频时是 720000，一章超过约 99 分钟（2^32 / 720000 秒），时长就放不进 stts 的 32 位
	public const uint Timescale = 1000;

	// Nero chpl 的章节数和标题长度都只有一个字节
	private const int MaxNeroChapters = 255;
	private const int MaxNeroTitleBytes = 255;

	// 标题后面跟一个 encd 盒子，声明文本是 UTF-8
	private static ReadOnlySpan<byte> EncodingBox => [0x00, 0x00, 0x00, 0x0C, (byte)'e', (byte)'n', (byte)'c', (byte)'d', 0x00, 0x00, 0x01, 0x00];

	public static ChapterTrack? Create(IReadOnlyList<MediaChapter> chapters, uint movieTimescale, uint trackId)
	{
		if (chapters.Count is 0)
		{
			return null;
		}

		ChapterSample[] samples = new ChapterSample[chapters.Count];

		for (int index = 0; index < chapters.Count; ++index)
		{
			MediaChapter chapter = chapters[index];
			TimeSpan next = index + 1 < chapters.Count ? chapters[index + 1].Start : chapter.End;
			long duration = Math.Max(Rescale(next, Timescale) - Rescale(chapter.Start, Timescale), 0);

			if (duration > uint.MaxValue)
			{
				throw new UnsupportedLayoutException($"a chapter of {duration} ms");
			}

			string title = Normalize(chapter.Title);
			samples[index] = new ChapterSample((uint)duration, chapter.Start, title, CreateText(title));
		}

		return new ChapterTrack(trackId, samples, Rescale(chapters[0].Start, movieTimescale), Rescale(chapters.Max(static chapter => chapter.End), movieTimescale));
	}

	// 读取方大多把标题当作以空字符结尾的字符串，空字符之后的内容会丢，所以去掉；换行统一成 \n，和 FLV、TS 转换写出的章节标题一样
	private static string Normalize(string title)
	{
		return title.ReplaceLineEndings("\n").Replace("\0", string.Empty, StringComparison.Ordinal);
	}

	private static long Rescale(TimeSpan time, uint timescale)
	{
		return TimeRescale.Near(time.Ticks, timescale, TimeSpan.TicksPerSecond);
	}

	// 两字节长度之后是 UTF-8 标题；Utf8.FromUtf16 在放不下时只写完整的字符，太长的标题截在字符边界上
	private static byte[] CreateText(string title)
	{
		int capacity = Math.Min(Encoding.UTF8.GetByteCount(title), ushort.MaxValue);
		byte[] data = new byte[2 + capacity + EncodingBox.Length];
		Utf8.FromUtf16(title, data.AsSpan(2, capacity), out _, out int length);
		Array.Resize(ref data, 2 + length + EncodingBox.Length);
		BinaryPrimitives.WriteUInt16BigEndian(data, (ushort)length);
		EncodingBox.CopyTo(data.AsSpan(2 + length));
		return data;
	}

	// 版本 1 在章节数前有 4 字节保留字段；开始时间按 100 纳秒计，和 TimeSpan 的刻度相同
	public static void WriteNeroChapters(BoxWriter writer, ReadOnlySpan<ChapterSample> samples)
	{
		Span<byte> title = stackalloc byte[MaxNeroTitleBytes];
		samples = samples.Slice(0, Math.Min(samples.Length, MaxNeroChapters));

		writer.BeginFull(BoxType.Chpl, 1);
		writer.WriteUInt32(0);
		writer.WriteUInt8((byte)samples.Length);

		foreach (ChapterSample sample in samples)
		{
			Utf8.FromUtf16(sample.Title, title, out _, out int length);
			writer.WriteUInt64((ulong)sample.Start.Ticks);
			writer.WriteUInt8((byte)length);
			writer.Write(title.Slice(0, length));
		}

		writer.End();
	}
}
