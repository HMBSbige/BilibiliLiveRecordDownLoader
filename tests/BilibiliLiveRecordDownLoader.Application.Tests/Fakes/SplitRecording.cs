using BilibiliLive.Streams;
using BilibiliLiveRecordDownLoader.Application.Recording;
using BilibiliLiveRecordDownLoader.Application.Recording.Splitting;
using System.Collections.Concurrent;
using System.Globalization;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Fakes;

/// <summary>
/// 让脚本扮演会话，经过真实的写缓冲、分段器和写盘端录到内存文件里。第一段的文件名是 0，之后依次是 1、2……
/// </summary>
internal sealed class SplitRecording
{
	private readonly ConcurrentQueue<MemoryFile> _files = new();
	private readonly ConcurrentQueue<string> _completed = new();
	private readonly ConcurrentQueue<SegmentStart> _starts = new();

	public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

	// 打开第几个文件（从 1 开始）时失败
	public int? FailOpeningFile { get; init; }

	// 关闭第几个文件（从 0 开始）时失败
	public int? FailClosingFile { get; init; }

	public int MaxPendingLength { get; init; } = RecordingSegmenter.DefaultMaxPendingLength;

	// 关闭第几个文件（从 0 开始）时一直等到给出的任务完成
	public (int Index, Task Release)? BlockClosingFile { get; init; }

	public TimeSpan DrainTimeout { get; init; } = TimeSpan.FromSeconds(5);

	public IReadOnlyList<byte[]> Files => [.. _files.Select(static file => file.Data)];

	public IReadOnlyList<string> Completed => [.. _completed];

	public IReadOnlyList<SegmentStart> Starts => [.. _starts];

	// 录制中不改的设置
	public static SplitControl Fixed(RecordingSplit split, bool onTitleChange = false)
	{
		RecordingSplitPolicy policy = new(split, onTitleChange);
		return new SplitControl(() => policy);
	}

	public Task<StreamEndReason> RunAsync(MediaSplitter splitter, RecordingSplit split, Func<SessionWriter, Task> script, CancellationToken cancellationToken)
	{
		return RunAsync(splitter, Fixed(split), script, cancellationToken);
	}

	public async Task<StreamEndReason> RunAsync(MediaSplitter splitter, SplitControl control, Func<SessionWriter, Task> script, CancellationToken cancellationToken)
	{
		MemoryFile first = Create(0);
		SegmentedOutput output = new(first, "0", (writer, starts) => new RecordingSegmenter(writer, splitter, control, starts, Time, NullLogger.Instance) { MaxPendingLength = MaxPendingLength }, OpenNext, _completed.Enqueue);

		return await RecordingWriteBuffer.CopyAsync(new ScriptedSession(script, Time), output, new Progress<long>(), DrainTimeout, cancellationToken);

		(string Path, Stream File) OpenNext(SegmentStart start)
		{
			int index = _files.Count;

			if (index == FailOpeningFile)
			{
				throw new RecordingWriteException(new IOException("cannot create"));
			}

			_starts.Enqueue(start);
			return (index.ToString(CultureInfo.InvariantCulture), Create(index));
		}
	}

	// 被卡住的那个文件关闭完成之后返回
	public Task WaitForClosingAsync(CancellationToken cancellationToken)
	{
		return _files.ElementAt(BlockClosingFile?.Index ?? 0).Closed.WaitAsync(cancellationToken);
	}

	private MemoryFile Create(int index)
	{
		MemoryFile file = new(index == FailClosingFile, BlockClosingFile is (var blocked, var release) && blocked == index ? release : Task.CompletedTask);
		_files.Enqueue(file);
		return file;
	}

	private sealed class MemoryFile(bool failOnClose, Task release) : MemoryStream
	{
		private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public byte[] Data { get; private set; } = [];

		public Task Closed => _closed.Task;

		public override async ValueTask DisposeAsync()
		{
			Data = ToArray();
			await release;
			_closed.TrySetResult();

			if (failOnClose)
			{
				throw new IOException("cannot flush");
			}

			await base.DisposeAsync();
		}
	}

	private sealed class ScriptedSession(Func<SessionWriter, Task> script, FakeTimeProvider time) : LiveStreamSession
	{
		public override Uri Source { get; } = new("https://cdn.test/live");

		public override async Task<StreamEndReason> CopyToAsync(Stream destination, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
		{
			await script(new SessionWriter(destination, null, time, cancellationToken));
			return StreamEndReason.EndOfStream;
		}

		public override async Task<StreamEndReason> CopyToAsync(Stream destination, IHlsSegmentObserver segments, IProgress<long>? progress, CancellationToken cancellationToken)
		{
			await script(new SessionWriter(destination, segments, time, cancellationToken));
			return StreamEndReason.EndOfStream;
		}

		public override ValueTask DisposeAsync()
		{
			return ValueTask.CompletedTask;
		}
	}
}

internal sealed class SessionWriter(Stream destination, IHlsSegmentObserver? segments, FakeTimeProvider time, CancellationToken cancellationToken)
{
	public CancellationToken CancellationToken { get; } = cancellationToken;

	public async Task WriteAsync(byte[] data)
	{
		await destination.WriteAsync(data, CancellationToken);
	}

	// 和真实会话一样逐块写出，元素会被切在两次写入之间
	public async Task WriteInChunksAsync(byte[] data, int chunkSize)
	{
		for (int offset = 0; offset < data.Length; offset += chunkSize)
		{
			await destination.WriteAsync(data.AsMemory(offset, Math.Min(chunkSize, data.Length - offset)), CancellationToken);
		}
	}

	public async Task InitAsync(byte[] data)
	{
		segments?.OnInitSegment(data);
		await WriteAsync(data);
	}

	public async Task SegmentAsync(byte[] data)
	{
		segments?.OnMediaSegment(data);
		await WriteAsync(data);
	}

	public void Advance(TimeSpan duration)
	{
		time.Advance(duration);
	}
}
