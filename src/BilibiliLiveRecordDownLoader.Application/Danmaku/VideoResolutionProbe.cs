using System.Buffers.Binary;

namespace BilibiliLiveRecordDownLoader.Application.Danmaku;

internal static class VideoResolutionProbe
{
	// 流头在开头几个 tag 或几个 PAT/PMT/SPS 包里
	private const int HeadLength = 4 << 20;

	// 没做 faststart 的 MP4 把 moov 放在末尾，几小时的录像 moov 也就几十 MB
	private const long MaxMovieBoxLength = 64 << 20;

	public static async Task<VideoResolution?> ReadAsync(string path, CancellationToken cancellationToken)
	{
		await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous);

		return Path.GetExtension(path).ToLowerInvariant() switch
		{
			@".mp4" => await ReadMp4Async(stream, cancellationToken),
			@".flv" => ReadFlv(await ReadHeadAsync(stream, cancellationToken)),
			@".ts" => ReadTransportStream(await ReadHeadAsync(stream, cancellationToken)),
			_ => null
		};
	}

	private static async Task<byte[]> ReadHeadAsync(FileStream stream, CancellationToken cancellationToken)
	{
		byte[] head = new byte[(int)Math.Min(stream.Length, HeadLength)];
		await stream.ReadExactlyAsync(head, cancellationToken);
		return head;
	}

	private static VideoResolution? ReadFlv(byte[] head)
	{
		FlvSplitter splitter = new();
		int position = 0;

		// 遇到切分点时停在它前面，再调用一次就越过它；返回 false 时完整的元素都看过了
		while (splitter.Resolution is null && splitter.TryFindSplit(head, ref position))
		{
		}

		return splitter.Resolution;
	}

	// 录像从包边界开始，失去包同步之后就不再往后找；SPS 在关键帧前面，挨个看视频 PES 的开头
	private static VideoResolution? ReadTransportStream(ReadOnlySpan<byte> head)
	{
		ProgramTables tables = new();
		Span<byte> pes = stackalloc byte[TransportStream.ParameterSetCapacity];

		for (int offset = 0; offset + TransportStream.PacketSize <= head.Length && head[offset] is TransportStream.SyncByte; offset += TransportStream.PacketSize)
		{
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

	private static async Task<VideoResolution?> ReadMp4Async(FileStream stream, CancellationToken cancellationToken)
	{
		byte[] header = new byte[16];
		long offset = 0;

		while (offset + 8 <= stream.Length)
		{
			stream.Position = offset;
			await stream.ReadExactlyAsync(header.AsMemory(0, 8), cancellationToken);
			long size = BinaryPrimitives.ReadUInt32BigEndian(header);

			if (size is 1)
			{
				await stream.ReadExactlyAsync(header.AsMemory(8, 8), cancellationToken);
				size = (long)BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(8));
			}
			else if (size is 0)
			{
				size = stream.Length - offset;
			}

			// 超出文件的盒子之后不会再有完整的盒子；64 位长度可能是坏数据，写成相减才不会溢出
			if (size < 8 || size > stream.Length - offset)
			{
				return null;
			}

			if (header.AsSpan(4, 4).SequenceEqual("moov"u8))
			{
				if (size > MaxMovieBoxLength)
				{
					return null;
				}

				byte[] movie = new byte[size];
				stream.Position = offset;
				await stream.ReadExactlyAsync(movie, cancellationToken);
				return IsoBmff.TryReadMovie(movie, out Fmp4VideoTrack? video) ? video?.Resolution : null;
			}

			offset += size;
		}

		return null;
	}
}
