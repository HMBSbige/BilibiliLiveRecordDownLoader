using BilibiliLive.Danmu;
using BilibiliLiveRecordDownLoader.Application.Danmaku;
using System.Text;
using System.Xml;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Danmaku;

internal static class DanmakuSamples
{
	public static readonly DateTimeOffset SentAt = DateTimeOffset.FromUnixTimeSeconds(1795343190);

	public static DanmakuFileHeader Header { get; } = new()
	{
		Version = @"1.2.3",
		RoomId = 21452505,
		ShortId = 0,
		Streamer = @"测试主播",
		Title = @"测试标题",
		Area = @"虚拟主播",
		ParentArea = @"虚拟主播",
		LiveId = @"745899535714532510",
		LiveStartTime = new DateTimeOffset(2026, 11, 22, 18, 25, 55, TimeSpan.FromHours(8)),
		RecordStartTime = new DateTimeOffset(2026, 11, 22, 18, 26, 0, 123, TimeSpan.FromHours(8)),
		Offset = TimeSpan.Zero
	};

	public static DanmakuRecord.Comment Comment(double seconds, string text = @"文本", long uid = 123, int mode = 1)
	{
		return new DanmakuRecord.Comment(TimeSpan.FromMilliseconds(Math.Round(seconds * 1000)), mode, 25, 0xFFFFFF, SentAt, uid, @"观众", text, 0);
	}

	public static DanmakuRecord[] AllKinds()
	{
		return
		[
			new DanmakuRecord.Connection(TimeSpan.Zero, true, true),
			new DanmakuRecord.Comment(TimeSpan.FromMilliseconds(12345), 1, 25, 16777215, SentAt, 123, @"昵称", @"文本", 40685126698926083),
			new DanmakuRecord.Comment(TimeSpan.FromMilliseconds(13000), 5, 36, 0xFF0000, SentAt, 0, @"淡***", @"节奏风暴", 0) { UidHash = @"a16fe0dd", IsStorm = true },
			new DanmakuRecord.SuperChat(TimeSpan.FromSeconds(20), 456, @"老板", 30000, TimeSpan.FromSeconds(60), @"醒目留言") { Id = 20541912 },
			new DanmakuRecord.Gift(TimeSpan.FromMilliseconds(21500), 789, @"观众", @"小花花", 10, 1000),
			new DanmakuRecord.Guard(TimeSpan.FromSeconds(30), 1011, @"舰长", GuardLevel.Captain, 1, 198000),
			new DanmakuRecord.TitleChange(TimeSpan.FromSeconds(40), @"新标题"),
			new DanmakuRecord.Gap(TimeSpan.FromSeconds(100), TimeSpan.FromMilliseconds(105200)),
			new DanmakuRecord.Connection(TimeSpan.FromMilliseconds(105200), false, false),
			new DanmakuRecord.SuperChatDeletion(TimeSpan.FromSeconds(110), [20541912, 20541913]),
			new DanmakuRecord.Guard(TimeSpan.FromSeconds(120), 1012, @"老舰长", GuardLevel.Admiral, 3, 4_194_000) { IsRenewal = true }
		];
	}

	public static async Task<byte[]> WriteAsync(IEnumerable<DanmakuRecord> records, CancellationToken cancellationToken, DanmakuFileHeader? header = null)
	{
		MemoryStream stream = new();
		await using (DanmakuXmlWriter writer = await DanmakuXmlWriter.CreateAsync(new NonClosingStream(stream), header ?? Header, DanmakuFlushPolicy.Default, TimeProvider.System, cancellationToken))
		{
			foreach (DanmakuRecord record in records)
			{
				await writer.WriteAsync(record, cancellationToken);
			}
		}

		return stream.ToArray();
	}

	public static async Task<DanmakuFile> ReadAsync(byte[] content, CancellationToken cancellationToken)
	{
		using MemoryStream stream = new(content);
		return await DanmakuXmlReader.ReadAsync(stream, cancellationToken);
	}

	public static Task<DanmakuFile> ReadAsync(string xml, CancellationToken cancellationToken)
	{
		return ReadAsync(Encoding.UTF8.GetBytes(xml), cancellationToken);
	}

	public static Dictionary<string, int> ParseStrictly(byte[] content)
	{
		Dictionary<string, int> counts = new(StringComparer.Ordinal);
		using MemoryStream stream = new(content);
		using XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });

		while (reader.Read())
		{
			if (reader.NodeType is XmlNodeType.Element)
			{
				counts[reader.LocalName] = counts.GetValueOrDefault(reader.LocalName) + 1;
			}
		}

		return counts;
	}

	public static string[] Lines(byte[] content)
	{
		return Encoding.UTF8.GetString(content).Split('\n', StringSplitOptions.RemoveEmptyEntries);
	}

	// 测试要在写入者关闭后读 MemoryStream 的内容
	private sealed class NonClosingStream(Stream inner) : Stream
	{
		public override bool CanRead => inner.CanRead;

		public override bool CanSeek => inner.CanSeek;

		public override bool CanWrite => inner.CanWrite;

		public override long Length => inner.Length;

		public override long Position
		{
			get => inner.Position;
			set => inner.Position = value;
		}

		public override void Flush()
		{
			inner.Flush();
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			return inner.Read(buffer, offset, count);
		}

		public override long Seek(long offset, SeekOrigin origin)
		{
			return inner.Seek(offset, origin);
		}

		public override void SetLength(long value)
		{
			inner.SetLength(value);
		}

		public override void Write(byte[] buffer, int offset, int count)
		{
			inner.Write(buffer, offset, count);
		}
	}
}
