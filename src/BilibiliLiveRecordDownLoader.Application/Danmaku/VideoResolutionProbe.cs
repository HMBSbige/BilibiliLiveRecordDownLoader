using System.Buffers.Binary;

namespace BilibiliLiveRecordDownLoader.Application.Danmaku;

// 重新生成字幕时没有录制时的时间轴，只能从视频文件里读画面尺寸
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
			@".flv" => await ReadHeadAsync(stream, new FlvSplitter(), cancellationToken),
			@".ts" => await ReadHeadAsync(stream, new TransportStreamSplitter(), cancellationToken),
			_ => null
		};
	}

	private static async Task<VideoResolution?> ReadHeadAsync(FileStream stream, MediaSplitter splitter, CancellationToken cancellationToken)
	{
		byte[] head = new byte[(int)Math.Min(stream.Length, HeadLength)];
		await stream.ReadExactlyAsync(head, cancellationToken);
		int position = 0;

		// 遇到切分点时停在它前面，再调用一次就越过它
		while (splitter.Resolution is null && position < head.Length)
		{
			int before = position;

			if (!splitter.TryFindSplit(head, ref position) && position == before)
			{
				break;
			}
		}

		return splitter.Resolution;
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
			bool isMovie = header.AsSpan(4, 4).SequenceEqual("moov"u8);

			if (size is 1)
			{
				await stream.ReadExactlyAsync(header.AsMemory(8, 8), cancellationToken);
				size = (long)BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(8));
			}
			else if (size is 0)
			{
				size = stream.Length - offset;
			}

			if (size < 8)
			{
				return null;
			}

			if (isMovie)
			{
				if (size > MaxMovieBoxLength || offset + size > stream.Length)
				{
					return null;
				}

				byte[] movie = new byte[size];
				stream.Position = offset;
				await stream.ReadExactlyAsync(movie, cancellationToken);
				return IsoBmff.ReadVideoTrackTiming(movie)?.Resolution;
			}

			offset += size;
		}

		return null;
	}
}
