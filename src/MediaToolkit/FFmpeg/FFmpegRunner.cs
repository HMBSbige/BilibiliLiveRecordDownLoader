using System.ComponentModel;
using System.Text;

namespace MediaToolkit.FFmpeg;

public sealed class FFmpegRunner(string executablePath)
{
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

	public async Task RunAsync(IReadOnlyList<string> arguments, IProgress<FFmpegProgress>? progress = null, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(arguments);
		cancellationToken.ThrowIfCancellationRequested();

		using Process process = ChildProcessTracker.Start(new ProcessStartInfo(ExecutablePath, arguments)
		{
			CreateNoWindow = true,
			RedirectStandardInput = true,
			RedirectStandardError = true,
			StandardErrorEncoding = Encoding.UTF8
		});

		// 读取不能依赖调用方的同步上下文，UI 线程一忙 stderr 管道就会写满，把 FFmpeg 卡住
		Task<string?> readError = Task.Run(() => ReadStandardErrorAsync(process.StandardError, progress));

		try
		{
			await process.WaitForExitAsync(cancellationToken);
		}
		catch (OperationCanceledException)
		{
			await StopAsync(process);
			await ((Task)readError).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
			throw;
		}

		string? lastLine = await readError;

		if (process.ExitCode is not 0)
		{
			throw new FFmpegException(process.ExitCode, lastLine);
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

	private static async Task<string?> ReadStandardErrorAsync(StreamReader reader, IProgress<FFmpegProgress>? progress)
	{
		string? lastLine = null;
		FFmpegProgress current = default;

		// 统计行以 \r 结尾，ReadLine 会把它当作一行
		while (await reader.ReadLineAsync() is { } line)
		{
			if (string.IsNullOrWhiteSpace(line))
			{
				continue;
			}

			lastLine = line;
			current = FFmpegOutputParser.ParseProgress(line, current);
			progress?.Report(current);
		}

		return lastLine;
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
			// 进程已经退出，管道已关闭
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
