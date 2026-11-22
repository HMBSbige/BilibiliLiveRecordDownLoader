using System.ComponentModel;
using System.Text;

namespace MediaToolkit.FFmpeg;

public sealed class FFmpegRunner(string executablePath)
{
	// 失败原因常在倒数几行，最后一行往往只是收尾提示
	private const int FailureLineCount = 3;

	internal static string ExecutableName { get; } = OperatingSystem.IsWindows() ? @"ffmpeg.exe" : @"ffmpeg";

	// 从 Finder 或桌面环境启动的 GUI 应用拿不到 shell 里配置的 PATH
	internal static IReadOnlyList<string> PlatformDirectories { get; } = OperatingSystem.IsMacOS() ? [@"/opt/homebrew/bin", @"/usr/local/bin"]
		: OperatingSystem.IsLinux() ? [@"/usr/bin", @"/usr/local/bin"]
		: [];

	public string ExecutablePath { get; } = executablePath;

	internal TimeSpan GracefulExitTimeout { get; init; } = TimeSpan.FromSeconds(5);

	public static string? Locate(string? preferredPath)
	{
		return Locate(preferredPath, AppContext.BaseDirectory, Environment.GetEnvironmentVariable(@"PATH"), PlatformDirectories);
	}

	internal static string? Locate(string? preferredPath, string applicationDirectory, string? pathVariable, IReadOnlyList<string> platformDirectories)
	{
		IEnumerable<string> pathDirectories = SplitPathVariable(pathVariable);

		if (string.IsNullOrWhiteSpace(preferredPath))
		{
			return FindInDirectories(ExecutableName, [applicationDirectory, .. pathDirectories, .. platformDirectories]);
		}

		if (File.Exists(preferredPath))
		{
			return Path.GetFullPath(preferredPath);
		}

		if (Path.GetFileName(preferredPath) != preferredPath)
		{
			return null;
		}

		string command = OperatingSystem.IsWindows() && !Path.HasExtension(preferredPath) ? preferredPath + @".exe" : preferredPath;

		return FindInDirectories(command, [.. pathDirectories, .. platformDirectories]);
	}

	public async Task<string?> GetVersionAsync(CancellationToken cancellationToken = default)
	{
		Process process;

		try
		{
			process = ChildProcessTracker.Start(new ProcessStartInfo(ExecutablePath, [@"-version"])
			{
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				StandardOutputEncoding = Encoding.UTF8
			});
		}
		catch (Win32Exception)
		{
			return null;
		}

		using (process)
		{
			try
			{
				string output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
				await process.WaitForExitAsync(cancellationToken);

				return process.ExitCode is 0 ? FFmpegOutputParser.ParseVersion(output) : null;
			}
			catch (OperationCanceledException)
			{
				process.Kill(true);
				throw;
			}
		}
	}

	public Task RunAsync(IReadOnlyList<string> arguments, IProgress<FFmpegProgress>? progress = null, CancellationToken cancellationToken = default)
	{
		return RunCoreAsync(arguments, null, progress, cancellationToken);
	}

	// readOutput 要读到流结束，取消后 FFmpeg 收尾期间的输出也交给它
	public Task RunAsync(IReadOnlyList<string> arguments, Func<Stream, Task> readOutput, IProgress<FFmpegProgress>? progress = null, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(readOutput);

		return RunCoreAsync(arguments, readOutput, progress, cancellationToken);
	}

	private async Task RunCoreAsync(IReadOnlyList<string> arguments, Func<Stream, Task>? readOutput, IProgress<FFmpegProgress>? progress, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(arguments);
		cancellationToken.ThrowIfCancellationRequested();

		using CancellationTokenSource stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		ProcessStartInfo startInfo = new(ExecutablePath, arguments)
		{
			CreateNoWindow = true,
			RedirectStandardInput = true,
			RedirectStandardOutput = readOutput is not null,
			RedirectStandardError = true,
			StandardErrorEncoding = Encoding.UTF8
		};
		RemoveUnrequestedProxy(startInfo);
		using Process process = ChildProcessTracker.Start(startInfo);

		// 读取不能依赖调用方的同步上下文，UI 线程一忙 stderr 管道就会写满，把 FFmpeg 卡住
		Task<string?> readError = Task.Run(() => ReadStandardErrorAsync(process.StandardError, progress, stopping));
		Task readingOutput = readOutput is null ? Task.CompletedTask : Task.Run(() => ReadStandardOutputAsync(process.StandardOutput.BaseStream, readOutput, stopping));

		try
		{
			await process.WaitForExitAsync(stopping.Token);
		}
		catch (OperationCanceledException)
		{
			await StopAsync(process);
			await Task.WhenAll(readError, readingOutput).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
			cancellationToken.ThrowIfCancellationRequested();
			await readingOutput;
			await readError;
			throw;
		}

		await readingOutput;
		string? failureMessage = await readError;

		if (process.ExitCode is not 0)
		{
			throw new FFmpegException(process.ExitCode, failureMessage);
		}
	}

	// 没传代理时不能让 FFmpeg 改读环境变量；只删小写名就够：Windows 上不区分大小写，其他系统 FFmpeg 只读小写
	internal static void RemoveUnrequestedProxy(ProcessStartInfo startInfo)
	{
		if (!startInfo.ArgumentList.Contains(@"-http_proxy"))
		{
			startInfo.Environment.Remove(@"http_proxy");
		}
	}

	private static IEnumerable<string> SplitPathVariable(string? pathVariable)
	{
		return pathVariable?.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(static directory => directory.Trim('"')) ?? [];
	}

	private static string? FindInDirectories(string fileName, IEnumerable<string> directories)
	{
		return directories
			.Where(static directory => !string.IsNullOrEmpty(directory))
			.Select(directory => Path.Combine(directory, fileName))
			.Where(File.Exists)
			.Select(Path.GetFullPath)
			.FirstOrDefault();
	}

	// 出错后也要排空管道，否则 FFmpeg 卡在写满的 stderr 上不退出
	private static async Task<string?> ReadStandardErrorAsync(StreamReader reader, IProgress<FFmpegProgress>? progress, CancellationTokenSource stopping)
	{
		Queue<string> failureLines = new(FailureLineCount);
		FFmpegProgress current = default;

		try
		{
			// 统计行以 \r 结尾，ReadLine 会把它当作一行
			while (await reader.ReadLineAsync() is { } rawLine)
			{
				if (string.IsNullOrWhiteSpace(rawLine))
				{
					continue;
				}

				string line = FFmpegOutputParser.RedactUrlQueries(rawLine);

				if (FFmpegOutputParser.IsDiagnostic(line))
				{
					if (failureLines.Count is FailureLineCount)
					{
						failureLines.Dequeue();
					}

					failureLines.Enqueue(line.Trim());
				}

				current = FFmpegOutputParser.ParseProgress(line, current);
				progress?.Report(current);
			}
		}
		catch
		{
			await stopping.CancelAsync();
			await reader.BaseStream.CopyToAsync(Stream.Null);
			throw;
		}

		return failureLines.Count is 0 ? null : string.Join(@" | ", failureLines);
	}

	// 出错后也要排空管道，否则 FFmpeg 卡在写满的 stdout 上不退出
	private static async Task ReadStandardOutputAsync(Stream output, Func<Stream, Task> readOutput, CancellationTokenSource stopping)
	{
		try
		{
			await readOutput(output);
		}
		catch
		{
			await stopping.CancelAsync();
			await output.CopyToAsync(Stream.Null);
			throw;
		}
	}

	private async Task StopAsync(Process process)
	{
		try
		{
			// 让 FFmpeg 自己收尾写完文件尾，直接结束进程会留下无法播放的文件
			await process.StandardInput.WriteAsync('q');
			await process.StandardInput.FlushAsync();
		}
		catch (IOException)
		{
		}

		using CancellationTokenSource timeout = new(GracefulExitTimeout);

		try
		{
			await process.WaitForExitAsync(timeout.Token);
		}
		catch (OperationCanceledException)
		{
			process.Kill(true);
			await process.WaitForExitAsync(CancellationToken.None);
		}
	}
}
