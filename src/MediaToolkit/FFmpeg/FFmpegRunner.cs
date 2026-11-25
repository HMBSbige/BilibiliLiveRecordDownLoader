using System.ComponentModel;
using System.Text;

namespace MediaToolkit.FFmpeg;

public sealed class FFmpegRunner(string executablePath)
{
	private const int FailureLineCount = 3;

	internal static string ExecutableName { get; } = OperatingSystem.IsWindows() ? @"ffmpeg.exe" : @"ffmpeg";

	// 从 Finder 或桌面环境启动的 GUI 应用拿不到 shell 里配置的 PATH
	private static IReadOnlyList<string> PlatformDirectories { get; } = OperatingSystem.IsMacOS() ? [@"/opt/homebrew/bin", @"/usr/local/bin"]
		: OperatingSystem.IsLinux() ? [@"/usr/bin", @"/usr/local/bin"]
		: [];

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
		ProcessStartInfo startInfo = new(executablePath, [@"-version"])
		{
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			StandardOutputEncoding = Encoding.UTF8
		};
		Process process;

		try
		{
			process = ChildProcessTracker.Start(startInfo);
		}
		catch (Win32Exception)
		{
			return null;
		}

		using (process)
		{
			try
			{
				(string output, _) = await process.ReadAllTextAsync(cancellationToken);
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

	public async Task RunAsync(IReadOnlyList<string> arguments, IProgress<FFmpegProgress>? progress = null, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(arguments);
		cancellationToken.ThrowIfCancellationRequested();

		using CancellationTokenSource stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		ProcessStartInfo startInfo = new(executablePath, arguments)
		{
			CreateNoWindow = true,
			RedirectStandardInput = true,
			RedirectStandardError = true,
			StandardErrorEncoding = Encoding.UTF8
		};
		using Process process = ChildProcessTracker.Start(startInfo);

		// 读取不能依赖调用方的同步上下文，UI 线程一忙 stderr 管道就会写满，把 FFmpeg 卡住
		// 取消后也要把管道读完，所以不传取消令牌
		// ReSharper disable AccessToDisposedClosure
		Task<string?> readError = Task.Run(() => ReadStandardErrorAsync(process.StandardError, progress, stopping), CancellationToken.None);
		// ReSharper restore AccessToDisposedClosure

		try
		{
			await process.WaitForExitAsync(stopping.Token);
		}
		catch (OperationCanceledException)
		{
			await StopAsync(process);
			cancellationToken.ThrowIfCancellationRequested();
		}
		finally
		{
			// 读取用着 process 和 stopping，等它结束才能释放
			await ((Task)readError).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
		}

		// 不是调用方取消时，stopping 只会由读取失败触发，这里重新抛出那个异常
		string? failureMessage = await readError;

		if (process.ExitCode is not 0)
		{
			throw new FFmpegException(process.ExitCode, failureMessage);
		}
	}

	private static IEnumerable<string> SplitPathVariable(string? pathVariable)
	{
		return pathVariable?.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(static directory => directory.Trim('"')) ?? [];
	}

	// PATH 里的相对目录按当前目录解析，可能拾取到工作目录里伪造的 ffmpeg
	private static string? FindInDirectories(string fileName, IEnumerable<string> directories)
	{
		return directories
			.Where(Path.IsPathFullyQualified)
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
			while (await reader.ReadLineAsync(CancellationToken.None) is { } line)
			{
				if (string.IsNullOrWhiteSpace(line))
				{
					continue;
				}

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
			await reader.BaseStream.CopyToAsync(Stream.Null, CancellationToken.None);
			throw;
		}

		return failureLines.Count is 0 ? null : string.Join(@" | ", failureLines);
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
			// 配置的可能是包装脚本或 shim，真正的 FFmpeg 是它的子进程，只结束这一个进程管道不会关闭
			process.Kill(true);
			await process.WaitForExitAsync(CancellationToken.None);
		}
	}
}
