using Microsoft.Win32.SafeHandles;
using System.Buffers.Binary;
using System.Text;

namespace BilibiliLiveRecordDownLoader.Application.Danmaku;

internal static class VideoResolutionProbe
{
	// 流头在开头几个 tag 或几个 PAT/PMT/SPS 包里
	private const int HeadLength = 4 << 20;

	public static async Task<VideoResolution?> ReadAsync(string path, CancellationToken cancellationToken)
	{
		using SafeFileHandle file = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileOptions.Asynchronous);

		return Path.GetExtension(path).ToLowerInvariant() switch
		{
			@".mp4" => await new Mp4Reader(file, cancellationToken).ReadAsync(),
			@".flv" => ReadFlv((await ReadHeadAsync(file, cancellationToken)).Span),
			@".ts" => ReadTransportStream((await ReadHeadAsync(file, cancellationToken)).Span),
			_ => null
		};
	}

	private static async Task<ReadOnlyMemory<byte>> ReadHeadAsync(SafeFileHandle file, CancellationToken cancellationToken)
	{
		byte[] head = GC.AllocateUninitializedArray<byte>((int)Math.Min(RandomAccess.GetLength(file), HeadLength));
		return head.AsMemory(0, await RandomAccess.ReadAsync(file, head, 0, cancellationToken));
	}

	private static VideoResolution? ReadFlv(ReadOnlySpan<byte> head)
	{
		FlvSplitter splitter = new();
		int position = 0;

		// 遇到切分点时停在它前面，再调用一次就越过它；返回 false 时完整的元素都看过了
		while (splitter.Resolution is null && splitter.TryFindSplit(head, ref position))
		{
		}

		return splitter.Resolution;
	}

	// HLS 分片开头不到一个包的杂字节也原样写进了录像，开头和失去包同步之后都要重新找包边界；SPS 在关键帧前面，挨个看视频 PES 的开头
	private static VideoResolution? ReadTransportStream(ReadOnlySpan<byte> head)
	{
		ProgramTables tables = new();
		Span<byte> pes = stackalloc byte[TransportStream.ParameterSetCapacity];

		for (int offset = TsHlsSplitter.FindFirstPacket(head); offset + TransportStream.PacketSize <= head.Length; offset += TransportStream.PacketSize)
		{
			if (head[offset] is not TransportStream.SyncByte)
			{
				// 当前位置不是包头，返回 0 只能是没找到
				if (TsHlsSplitter.FindFirstPacket(head.Slice(offset)) is not (> 0 and var skipped))
				{
					return null;
				}

				offset += skipped;
			}

			ReadOnlySpan<byte> packet = head.Slice(offset, TransportStream.PacketSize);

			if (tables.Inspect(packet) || tables is not { VideoPid: >= 0 and var videoPid, VideoSyntax: { } syntax } || TransportStream.GetPid(packet) != videoPid || !TransportStream.IsPayloadStart(packet))
			{
				continue;
			}

			int length = TransportStream.CopyFirstPes(head.Slice(offset), videoPid, pes);

			if (ParameterSets.ReadAnnexB(TransportStream.SkipPesHeader(pes.Slice(0, length)), syntax) is { } resolution)
			{
				return resolution;
			}
		}

		return null;
	}

	// 普通 MP4 的 moov 带着完整的样本表，随时长增长，长录像的有几十上百 MB；只按盒子头逐层往下找，只读 hdlr 和 stsd
	private sealed class Mp4Reader(SafeFileHandle file, CancellationToken cancellationToken)
	{
		// hdlr 和 stsd 只有几十到几千字节
		private const int MaxLeafLength = 64 << 10;

		private readonly byte[] _header = new byte[16];

		public async Task<VideoResolution?> ReadAsync()
		{
			if (await FindAsync(new Box(0, RandomAccess.GetLength(file)), @"moov") is not { } moov)
			{
				return null;
			}

			for (Box rest = moov; await FindAsync(rest, @"trak") is { } trak; rest = rest with { Start = trak.End })
			{
				if (await FindAsync(trak, @"mdia") is not { } mdia || await FindAsync(mdia, @"hdlr") is not { } hdlr || !IsVideo(await ReadContentAsync(hdlr)))
				{
					continue;
				}

				// 只看第一条视频轨
				return await FindAsync(mdia, @"minf") is { } minf && await FindAsync(minf, @"stbl") is { } stbl && await FindAsync(stbl, @"stsd") is { } stsd ? ReadSampleEntrySize(await ReadContentAsync(stsd)) : null;
			}

			return null;
		}

		private static bool IsVideo(ReadOnlySpan<byte> hdlr)
		{
			return hdlr.Length >= 12 && hdlr.Slice(8, 4).SequenceEqual("vide"u8);
		}

		// stsd 是 FullBox，版本、标志和 entry_count 之后是样本项；VisualSampleEntry 的保留字段、数据引用索引和预定义字段之后是 16 位的宽和高
		private static VideoResolution? ReadSampleEntrySize(ReadOnlySpan<byte> stsd)
		{
			int offset = 8;

			return stsd.Length >= 8 && BinaryPrimitives.ReadUInt32BigEndian(stsd.Slice(4)) > 0 && IsoBmff.TryReadBox(stsd, ref offset, out _, out ReadOnlySpan<byte> entry) && entry.Length >= 28
				? VideoResolution.Create(BinaryPrimitives.ReadUInt16BigEndian(entry.Slice(24)), BinaryPrimitives.ReadUInt16BigEndian(entry.Slice(26)))
				: null;
		}

		// 返回 parent 里第一个 type 盒子的内容范围
		private async Task<Box?> FindAsync(Box parent, string type)
		{
			for (long offset = parent.Start; offset + 8 <= parent.End;)
			{
				if (await RandomAccess.ReadAsync(file, _header.AsMemory(0, 8), offset, cancellationToken) < 8)
				{
					return null;
				}

				long size = BinaryPrimitives.ReadUInt32BigEndian(_header);
				int headerSize = 8;

				if (size is 1)
				{
					if (await RandomAccess.ReadAsync(file, _header.AsMemory(8, 8), offset + 8, cancellationToken) < 8)
					{
						return null;
					}

					size = (long)BinaryPrimitives.ReadUInt64BigEndian(_header.AsSpan(8));
					headerSize = 16;
				}
				else if (size is 0)
				{
					size = parent.End - offset;
				}

				// 超出父盒子的盒子之后不会再有完整的盒子；64 位长度可能是坏数据，写成相减才不会溢出
				if (size < headerSize || size > parent.End - offset)
				{
					return null;
				}

				if (Ascii.Equals(_header.AsSpan(4, 4), type))
				{
					return new Box(offset + headerSize, offset + size);
				}

				offset += size;
			}

			return null;
		}

		// 太大或没读满时返回空，按读不出内容处理
		private async Task<byte[]> ReadContentAsync(Box box)
		{
			if (box.End - box.Start > MaxLeafLength)
			{
				return [];
			}

			byte[] content = new byte[box.End - box.Start];
			return await RandomAccess.ReadAsync(file, content, box.Start, cancellationToken) == content.Length ? content : [];
		}

		// 盒子内容在文件里的范围
		private readonly record struct Box(long Start, long End);
	}
}
