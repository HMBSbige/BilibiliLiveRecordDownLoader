using MediaToolkit.FFmpeg;
using System.Collections.Concurrent;
using System.Text;

namespace MediaToolkit.Tests.FFmpeg;

public class FFmpegRunnerTest
{
	private const int TestTimeout = 60_000;

	[Test]
	public async Task ProxyFromTheEnvironmentIsDroppedWithoutTheProxyArgumentAsync()
	{
		ProcessStartInfo startInfo = new(@"ffmpeg", [@"-i", @"https://example.com/live.flv"]);
		startInfo.Environment[@"http_proxy"] = @"http://127.0.0.1:7890/";

		FFmpegRunner.RemoveUnrequestedProxy(startInfo);

		await Assert.That(startInfo.Environment).DoesNotContainKey(@"http_proxy");
	}

	[Test]
	public async Task UpperCaseProxyVariableIsDroppedOnWindowsAsync()
	{
		ProcessStartInfo startInfo = new(@"ffmpeg", [@"-i", @"https://example.com/live.flv"]);
		startInfo.Environment[@"HTTP_PROXY"] = @"http://127.0.0.1:7890/";

		FFmpegRunner.RemoveUnrequestedProxy(startInfo);

		// 其他系统上环境变量名区分大小写，FFmpeg 也不读大写的
		await Assert.That(startInfo.Environment.ContainsKey(@"HTTP_PROXY")).IsEqualTo(!OperatingSystem.IsWindows());
	}

	[Test]
	[Timeout(TestTimeout)]
	public async Task CancellationSendsQuitAndWaitsForGracefulExitAsync(CancellationToken cancellationToken)
	{
		// 比下面断言的 20 秒长，走了强杀路径就会失败
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

		await Assert.That(() => runner.RunAsync(arguments, progress, cts.Token)).Throws<OperationCanceledException>();

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

		await Assert.That(() => runner.RunAsync(arguments, progress, cts.Token)).Throws<OperationCanceledException>();

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

		await Assert.That(() => runner.RunAsync(arguments, ReadAsync, null, cts.Token)).Throws<OperationCanceledException>();

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

		await Assert.That(() => runner.RunAsync(arguments, FailAsync, progress, cancellationToken)).Throws<IOException>().WithMessage(@"disk full");
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

		await Assert.That(() => runner.RunAsync([], null, new CancellationToken(true))).Throws<OperationCanceledException>();
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

		await Assert.That(() => runner.RunAsync(arguments, null, cancellationToken))
			.Throws<FFmpegException>()
			.WithMessage(@"FFmpeg exited with code 3: first | boom")
			.And.Member(static e => e.ExitCode, static code => code.IsEqualTo(3))
			.And.Member(static e => e.LastMessage, static last => last.IsEqualTo(@"first | boom"));
	}

	[Test]
	[Timeout(TestTimeout)]
	public async Task FailureMessageKeepsTheReasonBeforeClosingNoticesAsync(CancellationToken cancellationToken)
	{
		FFmpegRunner runner = new(FakeProcess.Executable);
		// 和真实 FFmpeg 一样：原因之后还有一条 time=N/A 的统计行和固定的收尾提示
		IReadOnlyList<string> arguments = FakeProcess.Arguments(
			"""
			[Console]::Error.WriteLine('  Stream #0:0 -> #0:0 (copy)')
			[Console]::Error.WriteLine('[wav @ 0001] wav muxer does not support any stream of type video')
			[Console]::Error.WriteLine('[out#0/wav @ 0002] Could not write header: Invalid argument')
			[Console]::Error.Write("frame=    0 fps=0.0 q=-1.0 Lsize=N/A time=N/A bitrate=N/A speed=N/A`r")
			[Console]::Error.WriteLine('')
			[Console]::Error.WriteLine('Conversion failed!')
			exit 1
			""",
			"""
			printf '  Stream #0:0 -> #0:0 (copy)\n[wav @ 0001] wav muxer does not support any stream of type video\n[out#0/wav @ 0002] Could not write header: Invalid argument\nframe=    0 fps=0.0 q=-1.0 Lsize=N/A time=N/A bitrate=N/A speed=N/A\r\nConversion failed!\n' >&2
			exit 1
			""");

		await Assert.That(() => runner.RunAsync(arguments, null, cancellationToken))
			.Throws<FFmpegException>()
			.And.Member(static e => e.LastMessage, static last => last.IsEqualTo(@"Stream #0:0 -> #0:0 (copy) | [wav @ 0001] wav muxer does not support any stream of type video | [out#0/wav @ 0002] Could not write header: Invalid argument"));
	}

	[Test]
	[Timeout(TestTimeout)]
	public async Task SignedUrlQueriesAreRedactedFromFailuresAndProgressAsync(CancellationToken cancellationToken)
	{
		FFmpegRunner runner = new(FakeProcess.Executable);
		IReadOnlyList<string> arguments = FakeProcess.Arguments(
			"""
			[Console]::Error.WriteLine("[hls @ 0001] Opening 'https://cdn.test/live/1.m4s?sign=abc&uid=42' for reading")
			[Console]::Error.WriteLine('Error opening input file https://cdn.test/live/a.flv?expires=1&sign=abc&uid=42.')
			exit 1
			""",
			"""
			echo "[hls @ 0001] Opening 'https://cdn.test/live/1.m4s?sign=abc&uid=42' for reading" >&2
			echo 'Error opening input file https://cdn.test/live/a.flv?expires=1&sign=abc&uid=42.' >&2
			exit 1
			""");
		List<string> messages = [];

		await Assert.That(() => runner.RunAsync(arguments, new SyncProgress<FFmpegProgress>(p => messages.Add(p.Message)), cancellationToken))
			.Throws<FFmpegException>()
			.WithMessageContaining(@"https://cdn.test/live/a.flv?…")
			.And.WithMessageContaining(@"'https://cdn.test/live/1.m4s?…' for reading")
			.And.WithMessageNotContaining(@"sign=")
			.And.WithMessageNotContaining(@"uid=");
		await Assert.That(messages).IsNotEmpty().And.All(static message => !message.Contains(@"sign=", StringComparison.Ordinal));
	}

	[Test]
	[Timeout(TestTimeout)]
	public async Task ProgressFailureStopsTheProcessAndSurfacesAsync(CancellationToken cancellationToken)
	{
		FFmpegRunner runner = new(FakeProcess.Executable) { GracefulExitTimeout = TimeSpan.FromMilliseconds(500) };
		// 写满 stderr 管道后，没人读就会一直卡住
		IReadOnlyList<string> arguments = FakeProcess.Arguments(
			"""
			[Console]::Error.WriteLine("pid=$PID")
			while ($true) { [Console]::Error.WriteLine('x' * 1024) }
			""",
			"""
			echo "pid=$$" >&2
			exec yes >&2
			""");
		int processId = 0;
		SyncProgress<FFmpegProgress> progress = new(p =>
		{
			processId = int.Parse(p.Message.AsSpan(4));
			throw new InvalidOperationException(@"progress failed");
		});

		await Assert.That(() => runner.RunAsync(arguments, progress, cancellationToken)).Throws<InvalidOperationException>().WithMessage(@"progress failed");
		await Assert.That(processId).IsNotEqualTo(0);
		await Assert.That(FakeProcess.IsRunning(processId)).IsFalse();
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
		await Assert.That(reports).IsEquivalentTo(expected, CollectionOrdering.Matching);
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
		(bool Tracked, bool Untracked) tracking = (ChildProcessTracker.IsTracked(tracked), ChildProcessTracker.IsTracked(untracked));

		tracked.Kill();
		untracked.Kill();
		await tracked.WaitForExitAsync(cancellationToken);
		await untracked.WaitForExitAsync(cancellationToken);

		await Assert.That(tracking).IsEqualTo((true, false));

		static ProcessStartInfo SleepStartInfo()
		{
			return new ProcessStartInfo(FakeProcess.Executable, FakeProcess.Arguments(@"Start-Sleep -Seconds 60", @"exec sleep 60")) { CreateNoWindow = true };
		}
	}
}
