using MediaToolkit.FFmpeg;
using System.Collections.Concurrent;
using System.Globalization;

namespace MediaToolkit.Tests.FFmpeg;

public class FFmpegRunnerTest
{
	[Test]
	public async Task CancellationSendsQuitAndWaitsForGracefulExitAsync(CancellationToken cancellationToken)
	{
		// 留足时限：负载高时误走强杀就读不到 stdin=q
		FFmpegRunner runner = new(FakeProcess.Executable) { GracefulExitTimeout = TimeSpan.FromSeconds(30) };
		IReadOnlyList<string> arguments = FakeProcess.Arguments
		(
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
			"""
		);
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

		await Assert.That(() => runner.RunAsync(arguments, progress, cts.Token)).Throws<OperationCanceledException>();

		await Assert.That(messages).Contains(@"stdin=q");
	}

	[Test]
	public async Task CancellationKillsProcessThatIgnoresQuitAsync(CancellationToken cancellationToken)
	{
		FFmpegRunner runner = new(FakeProcess.Executable) { GracefulExitTimeout = TimeSpan.FromMilliseconds(500) };
		// 睡得远比测试超时久，没被强杀就不会自己退出
		IReadOnlyList<string> arguments = FakeProcess.Arguments
		(
			"""
			[Console]::Error.WriteLine("pid=$PID")
			Start-Sleep -Seconds 600
			""",
			"""
			echo "pid=$$" >&2
			exec sleep 600
			"""
		);
		using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		int processId = 0;
		SyncProgress<FFmpegProgress> progress = new(p =>
		{
			if (p.Message.StartsWith(@"pid=", StringComparison.Ordinal))
			{
				processId = int.Parse(p.Message.AsSpan(4), CultureInfo.InvariantCulture);
				cts.Cancel();
			}
		});

		await Assert.That(() => runner.RunAsync(arguments, progress, cts.Token)).Throws<OperationCanceledException>();

		await Assert.That(processId).IsNotEqualTo(0);
		await Assert.That(FakeProcess.IsRunning(processId)).IsFalse();
	}

	[Test]
	public async Task AlreadyCancelledTokenDoesNotStartProcessAsync()
	{
		FFmpegRunner runner = new(@"this-executable-does-not-exist");

		await Assert.That(() => runner.RunAsync([], null, new CancellationToken(true))).Throws<OperationCanceledException>();
	}

	[Test]
	public async Task NonZeroExitThrowsWithLastStandardErrorLineAsync(CancellationToken cancellationToken)
	{
		FFmpegRunner runner = new(FakeProcess.Executable);
		IReadOnlyList<string> arguments = FakeProcess.Arguments
		(
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
			"""
		);

		await Assert.That(() => runner.RunAsync(arguments, null, cancellationToken))
			.Throws<FFmpegException>()
			.WithMessage(@"FFmpeg exited with code 3: first | boom")
			.And.Member(static e => e.ExitCode, static code => code.IsEqualTo(3))
			.And.Member(static e => e.LastMessage, static last => last.IsEqualTo(@"first | boom"));
	}

	[Test]
	public async Task FailureMessageKeepsTheReasonBeforeClosingNoticesAsync(CancellationToken cancellationToken)
	{
		FFmpegRunner runner = new(FakeProcess.Executable);
		// 和真实 FFmpeg 一样：原因之后还有一条 time=N/A 的统计行和固定的收尾提示
		IReadOnlyList<string> arguments = FakeProcess.Arguments
		(
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
			"""
		);

		await Assert.That(() => runner.RunAsync(arguments, null, cancellationToken))
			.Throws<FFmpegException>()
			.And.Member(static e => e.LastMessage, static last => last.IsEqualTo(@"Stream #0:0 -> #0:0 (copy) | [wav @ 0001] wav muxer does not support any stream of type video | [out#0/wav @ 0002] Could not write header: Invalid argument"));
	}

	[Test]
	public async Task ProgressFailureStopsTheProcessAndSurfacesAsync(CancellationToken cancellationToken)
	{
		FFmpegRunner runner = new(FakeProcess.Executable) { GracefulExitTimeout = TimeSpan.FromMilliseconds(500) };
		IReadOnlyList<string> arguments = FakeProcess.Arguments
		(
			"""
			[Console]::Error.WriteLine("pid=$PID")
			while ($true) { [Console]::Error.WriteLine('x' * 1024) }
			""",
			"""
			echo "pid=$$" >&2
			exec yes >&2
			"""
		);
		int processId = 0;
		SyncProgress<FFmpegProgress> progress = new(p =>
		{
			processId = int.Parse(p.Message.AsSpan(4), CultureInfo.InvariantCulture);
			throw new InvalidOperationException(@"progress failed");
		});

		await Assert.That(() => runner.RunAsync(arguments, progress, cancellationToken)).Throws<InvalidOperationException>().WithMessage(@"progress failed");
		await Assert.That(processId).IsNotEqualTo(0);
		await Assert.That(FakeProcess.IsRunning(processId)).IsFalse();
	}

	[Test]
	public async Task ReportsProgressForEveryStandardErrorLineAsync(CancellationToken cancellationToken)
	{
		FFmpegRunner runner = new(FakeProcess.Executable);
		// 统计行和真实 FFmpeg 一样只以 \r 结尾
		IReadOnlyList<string> arguments = FakeProcess.Arguments
		(
			"""
			[Console]::Error.WriteLine('  Duration: 00:01:00.00, start: 0.000000')
			[Console]::Error.Write("frame=1 time=00:00:10.00 speed=1x`r")
			[Console]::Error.Write("frame=2 time=00:00:20.00 speed=1x`r")
			[Console]::Error.WriteLine('')
			[Console]::Error.WriteLine('done')
			""",
			"""
			printf '  Duration: 00:01:00.00, start: 0.000000\nframe=1 time=00:00:10.00 speed=1x\rframe=2 time=00:00:20.00 speed=1x\r\ndone\n' >&2
			"""
		);
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
	public async Task GetVersionAsyncParsesVersionFromStandardOutputAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string script = FakeProcess.CreateScript(directory, @"ffmpeg", "@echo ffmpeg version 1.2.3-fake Copyright (c) 2000-2026\r\n", "echo 'ffmpeg version 1.2.3-fake Copyright (c) 2000-2026'\n");

		string? version = await new FFmpegRunner(script).GetVersionAsync(cancellationToken);

		await Assert.That(version).IsEqualTo(@"1.2.3-fake");
	}

	[Test]
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
	public async Task GetVersionAsyncCancellationEndsTheWrappedProcessAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string script = FakeProcess.CreateScript
		(
			directory, @"ffmpeg",
			"@powershell -NoLogo -NoProfile -NonInteractive -Command \"Set-Content -LiteralPath '%~dp0pid.tmp' -Value $PID; Move-Item -LiteralPath '%~dp0pid.tmp' -Destination '%~dp0pid'; Start-Sleep -Seconds 600\"\r\n",
			"d=$(dirname \"$0\")\nsleep 600 &\necho $! > \"$d/pid.tmp\"\nmv \"$d/pid.tmp\" \"$d/pid\"\nwait\n"
		);
		using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

		Task<string?> getting = new FFmpegRunner(script).GetVersionAsync(cts.Token);
		using Process wrapped = await WaitForProcessIdFileAsync(directory.Combine(@"pid"), cancellationToken);

		try
		{
			await cts.CancelAsync();

			await Assert.That(() => getting).Throws<OperationCanceledException>();
			await wrapped.WaitForExitAsync(cancellationToken);
		}
		finally
		{
			wrapped.Kill();
		}
	}

	// 脚本先写临时文件再改名，文件出现时内容已经完整
	private static async Task<Process> WaitForProcessIdFileAsync(string path, CancellationToken cancellationToken)
	{
		while (!File.Exists(path))
		{
			await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
		}

		return Process.GetProcessById(int.Parse(await File.ReadAllTextAsync(path, cancellationToken), CultureInfo.InvariantCulture));
	}
}
