using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;

namespace BilibiliLiveRecordDownLoader.Application.Recording.Splitting;

// 网络端按单元决定换文件的位置，写盘端照着标记关闭旧文件、建新文件；关闭旧文件时网络端照常收进缓冲
// openNext 建下一段的文件，建不了时抛 RecordingWriteException；completed 收到已经关闭的一段
internal sealed class SegmentedOutput(Stream firstFile, string firstPath, Func<PipeWriter, ConcurrentQueue<SegmentStart>, RecordingSegmenter> createSegmenter, Func<SegmentStart, (string Path, Stream File)> openNext, Action<string> completed) : RecordingOutput
{
	private readonly ConcurrentQueue<SegmentStart> _starts = new();
	private Stream? _file = firstFile;
	private string _path = firstPath;

	public override async Task<StreamEndReason> ReceiveAsync(LiveStreamSession session, PipeWriter writer, IProgress<long> progress, CancellationToken cancellationToken)
	{
		await using RecordingSegmenter segmenter = createSegmenter(writer, _starts);

		try
		{
			return segmenter.Splitter is IHlsSegmentObserver segments
				? await session.CopyToAsync(segmenter, segments, progress, cancellationToken)
				: await session.CopyToAsync(segmenter, progress, cancellationToken);
		}
		finally
		{
			// 停止录制和断流时攒着的数据也要写出去
			segmenter.Complete();
		}
	}

	public override async Task WriteAsync(PipeReader reader, CancellationToken cancellationToken)
	{
		long position = 0;

		while (true)
		{
			ReadResult result = await reader.ReadAsync(cancellationToken);
			ReadOnlySequence<byte> buffer = result.Buffer;

			// 标记总是先于它之后的数据放进队列，这里看到数据时一定也看得到标记
			while (_starts.TryPeek(out SegmentStart start) && start.Offset < position + buffer.Length)
			{
				ReadOnlySequence<byte> previous = buffer.Slice(0, start.Offset - position);
				await WriteAsync(previous, cancellationToken);
				buffer = buffer.Slice(previous.End);
				position = start.Offset;
				await SwitchAsync(start, cancellationToken);
				_starts.TryDequeue(out _);
			}

			await WriteAsync(buffer, cancellationToken);
			position += buffer.Length;
			reader.AdvanceTo(result.Buffer.End);

			if (result.IsCompleted)
			{
				break;
			}
		}

		await CloseAsync();
	}

	public override ValueTask AbandonAsync()
	{
		return _file is { } file ? AbandonAsync(file) : ValueTask.CompletedTask;
	}

	private async ValueTask WriteAsync(ReadOnlySequence<byte> data, CancellationToken cancellationToken)
	{
		Stream file = _file ?? throw new InvalidOperationException("No recording file is open.");

		foreach (ReadOnlyMemory<byte> segment in data)
		{
			await file.WriteAsync(segment, cancellationToken);
		}
	}

	// 关旧文件卡到排空超时时，录制尝试已经结束，旧文件由录制循环交给后处理，这里不能再交出或开始新的一段
	private async ValueTask SwitchAsync(SegmentStart start, CancellationToken cancellationToken)
	{
		await CloseAsync();
		cancellationToken.ThrowIfCancellationRequested();
		completed(_path);
		(_path, _file) = openNext(start);
	}

	// 关闭失败时不再重复关闭；这一段没有交出去，由录制循环在最后交给后处理
	private async ValueTask CloseAsync()
	{
		if (_file is { } file)
		{
			_file = null;
			await file.DisposeAsync();
		}
	}
}
