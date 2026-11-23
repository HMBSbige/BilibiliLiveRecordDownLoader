using BilibiliLiveRecordDownLoader.Application.Recording;
using System.Text;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class FFmpegStreamSessionTest
{
	private static readonly Uri Source = new("https://cdn.test/live.flv");

	[Test]
	public async Task OutputFlushedAfterStoppingReachesTheRecordingAsync(CancellationToken cancellationToken)
	{
		using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		CapturingStream destination = new(written => written is "ready" ? stop.CancelAsync() : Task.CompletedTask);
		await using FFmpegStreamSession session = new(new FFmpegRunner(Executable), QuitEchoingArguments(), Source);

		await Assert.That(() => session.CopyToAsync(destination, null, stop.Token)).Throws<OperationCanceledException>();

		await Assert.That(destination.Text).IsEqualTo("ready|stdin=q");
	}

	[Test]
	public async Task WriteThatNeverReturnsCannotHangTheStopAsync(CancellationToken cancellationToken)
	{
		using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		TaskCompletionSource writeStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
		CapturingStream destination = new(async _ =>
		{
			writeStarted.TrySetResult();
			await Task.Delay(Timeout.Infinite, cancellationToken);
		});
		await using FFmpegStreamSession session = new(new FFmpegRunner(Executable), QuitEchoingArguments(), Source) { FlushTimeout = TimeSpan.FromMilliseconds(200) };

		Task copy = session.CopyToAsync(destination, null, stop.Token);
		await writeStarted.Task.WaitAsync(cancellationToken);
		await stop.CancelAsync();

		await Assert.That(() => copy.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken)).Throws<OperationCanceledException>();
	}

	private static string Executable => OperatingSystem.IsWindows() ? "powershell.exe" : "/bin/sh";

	private static IReadOnlyList<string> QuitEchoingArguments()
	{
		const string powerShell = """
			[Console]::Out.Write('ready')
			[Console]::Out.Flush()
			$c = [Console]::In.Read()
			[Console]::Out.Write('|stdin=' + [char]$c)
			exit 0
			""";
		const string shell = """
			printf ready
			c=$(head -c 1)
			printf "|stdin=$c"
			exit 0
			""";

		return OperatingSystem.IsWindows()
			? ["-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(powerShell))]
			: ["-c", shell];
	}

	private sealed class CapturingStream(Func<string, Task> onWrite) : Stream
	{
		private readonly Lock _lock = new();
		private readonly StringBuilder _text = new();

		public string Text
		{
			get
			{
				lock (_lock)
				{
					return _text.ToString();
				}
			}
		}

		public override bool CanRead => false;

		public override bool CanSeek => false;

		public override bool CanWrite => true;

		public override long Length => throw new NotSupportedException();

		public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

		public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
		{
			// 真实的文件流和管道在令牌取消后拒绝写入，要在记下内容之前检查
			cancellationToken.ThrowIfCancellationRequested();
			string text;

			lock (_lock)
			{
				text = _text.Append(Encoding.ASCII.GetString(buffer.Span)).ToString();
			}

			await onWrite(text).WaitAsync(cancellationToken);
		}

		public override void Write(byte[] buffer, int offset, int count)
		{
			throw new NotSupportedException();
		}

		public override void Flush()
		{
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			throw new NotSupportedException();
		}

		public override long Seek(long offset, SeekOrigin origin)
		{
			throw new NotSupportedException();
		}

		public override void SetLength(long value)
		{
			throw new NotSupportedException();
		}
	}
}
