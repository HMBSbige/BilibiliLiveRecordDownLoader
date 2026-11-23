using System.Buffers;
using System.IO.Pipelines;

namespace BilibiliLiveRecordDownLoader.Application.Recording.Splitting;

// 换文件在写盘端照着标记做，关闭旧文件时网络端照常收进缓冲；openNext 建不了文件时抛 RecordingWriteException
internal sealed class SegmentedOutput(Stream firstFile, string firstPath, Func<SegmentStarts.Writer, RecordingSegmenter> createSegmenter, Func<SegmentStart, (string Path, Stream File)> openNext, Action<string> completed) : RecordingOutput
{
	private readonly SegmentStarts _starts = new();
	private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private Stream? _file = firstFile;
	private string _path = firstPath;

	// 写盘端不再碰文件时完成；排空超时后会晚于这次录制尝试结束
	public Task Closed => _closed.Task;

	public override async Task<StreamEndReason> ReceiveAsync(LiveStreamSession session, PipeWriter writer, IProgress<long> progress, CancellationToken cancellationToken)
	{
		await using RecordingSegmenter segmenter = createSegmenter(_starts.CreateWriter(writer));

		try
		{
			return segmenter.Splitter is IHlsSegmentObserver segments
				? await session.CopyToAsync(segmenter, segments, progress, cancellationToken)
				: await session.CopyToAsync(segmenter, progress, cancellationToken);
		}
		finally
		{
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

			while (_starts.TryTake(position + buffer.Length, out SegmentStart start))
			{
				ReadOnlySequence<byte> previous = buffer.Slice(0, start.Offset - position);
				await WriteAsync(previous, cancellationToken);
				buffer = buffer.Slice(previous.End);
				position = start.Offset;
				await SwitchAsync(start, cancellationToken);
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
		_closed.TrySetResult();
	}

	public override async ValueTask AbandonAsync()
	{
		try
		{
			if (_file is { } file)
			{
				await AbandonAsync(file);
			}
		}
		finally
		{
			_closed.TrySetResult();
		}
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
