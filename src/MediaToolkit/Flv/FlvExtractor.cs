using System.Buffers;

namespace MediaToolkit.Flv;

public static class FlvExtractor
{
	private const int PreviousTagSizeLength = sizeof(uint);
	private const int TagPrefixLength = PreviousTagSizeLength + FlvTagHeader.Size;
	private const int ProgressSteps = 1000;

	private static readonly FileStreamOptions ReadOptions = new()
	{
		Mode = FileMode.Open,
		Access = FileAccess.Read,
		Share = FileShare.Read,
		Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
		BufferSize = 1 << 16
	};

	public static Task<FlvExtractResult> ExtractAsync(string inputPath, string outputDirectory, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrEmpty(inputPath);
		ArgumentException.ThrowIfNullOrEmpty(outputDirectory);

		string outputBasePath = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(inputPath));
		string videoPath = outputBasePath + @".h264";
		string audioPath = outputBasePath + @".aac";

		if (IsSamePath(inputPath, videoPath) || IsSamePath(inputPath, audioPath))
		{
			throw new ArgumentException(@"The input file would be overwritten by the output.", nameof(inputPath));
		}

		// 逐 tag 转换是 CPU 工作，不能让 await 回到调用方的 UI 线程上执行
		return Task.Run(() => ExtractCoreAsync(inputPath, outputDirectory, videoPath, audioPath, progress, cancellationToken), cancellationToken);
	}

	private static async Task<FlvExtractResult> ExtractCoreAsync(string inputPath, string outputDirectory, string videoPath, string audioPath, IProgress<double>? progress, CancellationToken cancellationToken)
	{
		await using FileStream input = new(inputPath, ReadOptions);
		Directory.CreateDirectory(outputDirectory);

		FlvTrack video = new(videoPath, new AvcAnnexBConverter());
		FlvTrack audio = new(audioPath, new AacAdtsConverter());

		double? frameRate;

		try
		{
			frameRate = await ExtractTagsAsync(input, video, audio, progress, cancellationToken);
			await video.DisposeAsync();
			await audio.DisposeAsync();
		}
		catch
		{
			await video.DiscardAsync();
			await audio.DiscardAsync();
			throw;
		}

		return new FlvExtractResult(video.OutputPath, audio.OutputPath, video.OutputPath is null ? null : frameRate);
	}

	private static async Task<double?> ExtractTagsAsync(FileStream input, FlvTrack video, FlvTrack audio, IProgress<double>? progress, CancellationToken cancellationToken)
	{
		long length = input.Length;
		byte[] prefix = new byte[TagPrefixLength];

		if (!await TryReadExactlyAsync(input, prefix.AsMemory(0, FlvHeader.Size), cancellationToken) || !FlvHeader.TryRead(prefix, out FlvHeader header))
		{
			throw new InvalidDataException(@"The input is not an FLV file.");
		}

		input.Seek(header.DataOffset, SeekOrigin.Begin);

		int reportedStep = -1;
		long frameCount = 0;
		uint firstFrameTimestamp = 0;
		uint lastFrameTimestamp = 0;
		byte[] data = ArrayPool<byte>.Shared.Rent(1 << 16);

		try
		{
			// 录制中断的文件最后一个 tag 往往不完整，读到哪里算哪里
			while (await TryReadExactlyAsync(input, prefix, cancellationToken))
			{
				cancellationToken.ThrowIfCancellationRequested();

				FlvTagHeader tag = FlvTagHeader.Read(prefix.AsSpan(PreviousTagSizeLength));

				if (tag.DataSize > data.Length)
				{
					ArrayPool<byte>.Shared.Return(data);
					data = ArrayPool<byte>.Shared.Rent(tag.DataSize);
				}

				Memory<byte> tagData = data.AsMemory(0, tag.DataSize);

				if (!await TryReadExactlyAsync(input, tagData, cancellationToken))
				{
					break;
				}

				switch (tag.Type)
				{
					case FlvTagType.Video:
					{
						await video.WriteAsync(tagData, cancellationToken);

						if (IsAvcFrame(tagData.Span))
						{
							firstFrameTimestamp = frameCount++ is 0 ? tag.Timestamp : firstFrameTimestamp;
							lastFrameTimestamp = tag.Timestamp;
						}

						break;
					}
					case FlvTagType.Audio:
					{
						await audio.WriteAsync(tagData, cancellationToken);
						break;
					}
				}

				ReportProgress();
			}
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(data);
		}

		ReportProgress();

		// 平均帧率让合并后的视频总时长与音频一致
		return frameCount > 1 && lastFrameTimestamp > firstFrameTimestamp ? (frameCount - 1) * 1000.0 / (lastFrameTimestamp - firstFrameTimestamp) : null;

		void ReportProgress()
		{
			if (progress is null)
			{
				return;
			}

			long position = Math.Min(input.Position, length);
			int step = (int)(position * ProgressSteps / length);

			if (step == reportedStep)
			{
				return;
			}

			reportedStep = step;
			progress.Report((double)position / length);
		}
	}

	// AVC 编码帧：CodecID 7，AVCPacketType 1
	private static bool IsAvcFrame(ReadOnlySpan<byte> tagData)
	{
		return tagData is [var flags, 1, ..] && (flags & 0x0F) is 7;
	}

	private static async ValueTask<bool> TryReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
	{
		return await stream.ReadAtLeastAsync(buffer, buffer.Length, false, cancellationToken) == buffer.Length;
	}

	private static bool IsSamePath(string path1, string path2)
	{
		StringComparison comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
		return string.Equals(Path.GetFullPath(path1), Path.GetFullPath(path2), comparison);
	}
}
