using MediaToolkit.FFmpeg;
using System.Collections.Concurrent;
using System.Text;

namespace MediaToolkit.Tests.FFmpeg;

public class FFmpegRunnerTest
{
	private const int TestTimeout = 60_000;

	[Test]
	[Timeout(TestTimeout)]
	public async Task CancellationSendsQuitAndWaitsForGracefulExitAsync(CancellationToken cancellationToken)
	{
		// 等待时间足够长，如果走的是强杀路径，测试会明显超时而不是碰巧通过
		FFmpegRunner runner = new(FakeProcess.Executable) { GracefulExitTimeout = TimeSpan.FromSeconds(30) };
		IReadOnlyList<string> arguments = FakeProcess.Arguments(
			"""
			[Console]::Error.WriteLine('ready')
			$c = [Console]::In.Read()
			[Console]::Error.WriteLine('stdin=' + [char]$c)
			exit 0
			""",
			"""
			echo ready >&2
			c=$(head -c 1)
			echo "stdin=$c" >&2
			exit 0
			""");
		using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		ConcurrentQueue<string> messages = [];
		SyncProgress<FFmpegProgress> progress = new(p =>
		{
			messages.Enqueue(p.Message);

			if (p.Message is @"ready")
			{
				cts.Cancel();
			}
		});
		Stopwatch stopwatch = Stopwatch.StartNew();

		await Assert.That(async () => await runner.RunAsync(arguments, progress, cts.Token)).Throws<OperationCanceledException>();

		await Assert.That(messages).Contains(@"stdin=q");
		await Assert.That(stopwatch.Elapsed).IsLessThan(TimeSpan.FromSeconds(20));
	}

	[Test]
	[Timeout(TestTimeout)]
	public async Task CancellationKillsProcessThatIgnoresQuitAsync(CancellationToken cancellationToken)
	{
		FFmpegRunner runner = new(FakeProcess.Executable) { GracefulExitTimeout = TimeSpan.FromMilliseconds(500) };
		IReadOnlyList<string> arguments = FakeProcess.Arguments(
			"""
			[Console]::Error.WriteLine("pid=$PID")
			Start-Sleep -Seconds 60
			""",
			"""
			echo "pid=$$" >&2
			exec sleep 60
			""");
		using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		int processId = 0;
		SyncProgress<FFmpegProgress> progress = new(p =>
		{
			if (p.Message.StartsWith(@"pid=", StringComparison.Ordinal))
			{
				processId = int.Parse(p.Message.AsSpan(4));
				cts.Cancel();
			}
		});
		Stopwatch stopwatch = Stopwatch.StartNew();

		await Assert.That(async () => await runner.RunAsync(arguments, progress, cts.Token)).Throws<OperationCanceledException>();

		await Assert.That(stopwatch.Elapsed).IsLessThan(TimeSpan.FromSeconds(30));
		await Assert.That(processId).IsNotEqualTo(0);
		await Assert.That(FakeProcess.IsRunning(processId)).IsFalse();
	}

	[Test]
	[Timeout(TestTimeout)]
	public async Task StandardOutputIsHandedToTheReaderAsync(CancellationToken cancellationToken)
	{
		FFmpegRunner runner = new(FakeProcess.Executable);
		IReadOnlyList<string> arguments = FakeProcess.Arguments(@"[Console]::Out.Write('ts-data')", @"printf ts-data");
		string output = string.Empty;

		await runner.RunAsync(arguments, async stdout => output = await new StreamReader(stdout).ReadToEndAsync(cancellationToken), null, cancellationToken);

		await Assert.That(output).IsEqualTo(@"ts-data");
	}

	[Test]
	[Timeout(TestTimeout)]
	public async Task CancellationLetsTheReaderReceiveOutputWrittenAfterQuitAsync(CancellationToken cancellationToken)
	{
		FFmpegRunner runner = new(FakeProcess.Executable) { GracefulExitTimeout = TimeSpan.FromSeconds(30) };
		IReadOnlyList<string> arguments = FakeProcess.Arguments(
			"""
			[Console]::Out.Write('ready')
			[Console]::Out.Flush()
			$c = [Console]::In.Read()
			[Console]::Out.Write('|stdin=' + [char]$c)
			exit 0
			""",
			"""
			printf ready
			c=$(head -c 1)
			printf "|stdin=$c"
			exit 0
			""");
		using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		StringBuilder output = new();

		await Assert.That(async () => await runner.RunAsync(arguments, ReadAsync, null, cts.Token)).Throws<OperationCanceledException>();

		await Assert.That(output.ToString()).IsEqualTo(@"ready|stdin=q");

		async Task ReadAsync(Stream stdout)
		{
			byte[] buffer = new byte[64];

			while (await stdout.ReadAsync(buffer, cancellationToken) is var read and > 0)
			{
				output.Append(Encoding.ASCII.GetString(buffer, 0, read));

				if (output.ToString() is @"ready")
				{
					await cts.CancelAsync();
				}
			}
		}
	}

	[Test]
	[Timeout(TestTimeout)]
	public async Task ReaderFailureStopsTheProcessAndSurfacesAsync(CancellationToken cancellationToken)
	{
		FFmpegRunner runner = new(FakeProcess.Executable) { GracefulExitTimeout = TimeSpan.FromMilliseconds(500) };
		IReadOnlyList<string> arguments = FakeProcess.Arguments(
			"""
			[Console]::Error.WriteLine("pid=$PID")
			while ($true) { [Console]::Out.Write('x' * 4096) }
			""",
			"""
			echo "pid=$$" >&2
			exec yes
			""");
		TaskCompletionSource<int> started = new(TaskCreationOptions.RunContinuationsAsynchronously);
		SyncProgress<FFmpegProgress> progress = new(p =>
		{
			if (p.Message.StartsWith(@"pid=", StringComparison.Ordinal))
			{
				started.TrySetResult(int.Parse(p.Message.AsSpan(4)));
			}
		});

		IOException? exception = await Assert.That(async () => await runner.RunAsync(arguments, FailAsync, progress, cancellationToken)).Throws<IOException>();

		await Assert.That(exception?.Message).IsEqualTo(@"disk full");
		await Assert.That(FakeProcess.IsRunning(await started.Task)).IsFalse();

		async Task FailAsync(Stream stdout)
		{
			await started.Task.WaitAsync(cancellationToken);
			throw new IOException(@"disk full");
		}
	}

	[Test]
	public async Task AlreadyCancelledTokenDoesNotStartProcessAsync()
	{
		FFmpegRunner runner = new(@"this-executable-does-not-exist");

		await Assert.That(async () => await runner.RunAsync([], null, new CancellationToken(true))).Throws<OperationCanceledException>();
	}

	[Test]
	[Timeout(TestTimeout)]
	public async Task NonZeroExitThrowsWithLastStandardErrorLineAsync(CancellationToken cancellationToken)
	{
		FFmpegRunner runner = new(FakeProcess.Executable);
		IReadOnlyList<string> arguments = FakeProcess.Arguments(
			"""
			[Console]::Error.WriteLine('first')
			[Console]::Error.WriteLine('boom')
			[Console]::Error.WriteLine('')
			exit 3
			""",
			"""
			echo first >&2
			echo boom >&2
			echo >&2
			exit 3
			""");

		FFmpegException? exception = await Assert.That(async () => await runner.RunAsync(arguments, null, cancellationToken)).Throws<FFmpegException>();

		await Assert.That(exception?.ExitCode).IsEqualTo(3);
		await Assert.That(exception?.Message).EndsWith(@": boom");
	}

	[Test]
	[Timeout(TestTimeout)]
	public async Task ReportsProgressForEveryStandardErrorLineAsync(CancellationToken cancellationToken)
	{
		FFmpegRunner runner = new(FakeProcess.Executable);
		// 统计行和真实 FFmpeg 一样只以 \r 结尾
		IReadOnlyList<string> arguments = FakeProcess.Arguments(
			"""
			[Console]::Error.WriteLine('  Duration: 00:01:00.00, start: 0.000000')
			[Console]::Error.Write("frame=1 time=00:00:10.00 speed=1x`r")
			[Console]::Error.Write("frame=2 time=00:00:20.00 speed=1x`r")
			[Console]::Error.WriteLine('')
			[Console]::Error.WriteLine('done')
			""",
			"""
			printf '  Duration: 00:01:00.00, start: 0.000000\nframe=1 time=00:00:10.00 speed=1x\rframe=2 time=00:00:20.00 speed=1x\r\ndone\n' >&2
			""");
		List<FFmpegProgress> reports = [];

		await runner.RunAsync(arguments, new SyncProgress<FFmpegProgress>(reports.Add), cancellationToken);

		TimeSpan duration = TimeSpan.FromMinutes(1);
		FFmpegProgress[] expected =
		[
			new(duration, null, @"  Duration: 00:01:00.00, start: 0.000000"),
			new(duration, TimeSpan.FromSeconds(10), @"frame=1 time=00:00:10.00 speed=1x"),
			new(duration, TimeSpan.FromSeconds(20), @"frame=2 time=00:00:20.00 speed=1x"),
			new(duration, TimeSpan.FromSeconds(20), @"done")
		];
		await Assert.That(reports).IsEquivalentTo(expected, EqualityComparer<FFmpegProgress>.Default, CollectionOrdering.Matching);
	}

	[Test]
	[Timeout(TestTimeout)]
	public async Task GetVersionAsyncParsesVersionFromStandardOutputAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string script = FakeProcess.CreateScript(directory, @"ffmpeg", "@echo ffmpeg version 1.2.3-fake Copyright (c) 2000-2026\r\n", "echo 'ffmpeg version 1.2.3-fake Copyright (c) 2000-2026'\n");

		string? version = await new FFmpegRunner(script).GetVersionAsync(cancellationToken);

		await Assert.That(version).IsEqualTo(@"1.2.3-fake");
	}

	[Test]
	[Timeout(TestTimeout)]
	public async Task GetVersionAsyncReturnsNullOnNonZeroExitAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string script = FakeProcess.CreateScript(directory, @"ffmpeg", "@echo ffmpeg version 1.2.3-fake\r\n@exit /b 1\r\n", "echo 'ffmpeg version 1.2.3-fake'\nexit 1\n");

		string? version = await new FFmpegRunner(script).GetVersionAsync(cancellationToken);

		await Assert.That(version).IsNull();
	}

	[Test]
	public async Task GetVersionAsyncReturnsNullForMissingExecutableAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();

		string? version = await new FFmpegRunner(directory.Combine(FFmpegRunner.ExecutableName)).GetVersionAsync(cancellationToken);

		await Assert.That(version).IsNull();
	}

	[Test]
	[Timeout(TestTimeout)]
	public async Task StartedProcessIsTrackedForCleanupAsync(CancellationToken cancellationToken)
	{
		using Process tracked = ChildProcessTracker.Start(SleepStartInfo());
		using Process untracked = Process.Start(SleepStartInfo()) ?? throw new InvalidOperationException();

		try
		{
			await Assert.That(ChildProcessTracker.IsTracked(tracked)).IsTrue();
			await Assert.That(ChildProcessTracker.IsTracked(untracked)).IsFalse();
		}
		finally
		{
			tracked.Kill();
			untracked.Kill();
			await tracked.WaitForExitAsync(cancellationToken);
			await untracked.WaitForExitAsync(cancellationToken);
		}

		static ProcessStartInfo SleepStartInfo()
		{
			return new ProcessStartInfo(FakeProcess.Executable, FakeProcess.Arguments(@"Start-Sleep -Seconds 60", @"exec sleep 60")) { CreateNoWindow = true };
		}
	}
}
